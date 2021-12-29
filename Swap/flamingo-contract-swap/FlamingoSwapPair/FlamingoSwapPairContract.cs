using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace FlamingoSwapPair
{
    [DisplayName("Flamingo Swap-Pair Contract")]
    [ManifestExtra("Author", "github.com/Hecate2")]
    [ManifestExtra("Email", "chenxinhao@ngd.neo.org")]
    [ManifestExtra("Description", "experimental FlamingoSwapPair")]
    [SupportedStandards("NEP-17")]
    [ContractPermission("*")]//avoid native contract hash change
    public partial class FlamingoSwapPairContract : SmartContract
    {

        /// <summary>
        /// https://uniswap.org/docs/v2/protocol-overview/smart-contracts/#minimum-liquidity
        /// </summary>
        const long MINIMUM_LIQUIDITY = 1000;

        public static BigInteger FIXED = 100_000_000_000_000_000;


        /// <summary>
        /// 合约初始化
        /// </summary>
        /// <param name="data"></param>
        /// <param name="update"></param>
        public static void _deploy(object data, bool update)
        {
            if (TokenA.ToUInteger() < TokenB.ToUInteger())
            {
                Token0 = TokenA;
                Token1 = TokenB;
            }
            else
            {
                Token0 = TokenB;
                Token1 = TokenA;
            }
            Deployed(Token0, Token1);
        }



        #region Token0,Token1


        /// <summary>
        /// Token 0 地址(Token0放置合约hash小的token)
        /// </summary>
        static UInt160 Token0
        {
            get => (UInt160)StorageGet("token0");
            set => StoragePut("token0", value);
        }


        /// <summary>
        ///  Token 1 地址
        /// </summary>
        static UInt160 Token1
        {
            get => (UInt160)StorageGet("token1");
            set => StoragePut("token1", value);
        }

        [Safe]
        public static UInt160 GetToken0()
        {
            return Token0;
        }

        [Safe]
        public static UInt160 GetToken1()
        {
            return Token1;
        }

        [Safe]
        public static BigInteger GetReserve0()
        {
            var r = ReservePair;
            return r.Reserve0;
        }

        [Safe]
        public static BigInteger GetReserve1()
        {
            var r = ReservePair;
            return r.Reserve1;
        }

        [Safe]
        public static PriceCumulative GetPriceCumulative()
        {
            return Cumulative;
        }

        [Safe]
        public static BigInteger Price0CumulativeLast()
        {
            return Cumulative.Price0CumulativeLast;
        }

        [Safe]
        public static BigInteger Price1CumulativeLast()
        {
            return Cumulative.Price1CumulativeLast;
        }
        #endregion


        public static class EnteredStorage
        {
            public static readonly string mapName = "entered";

            public static void Put(BigInteger value) => new StorageMap(Storage.CurrentContext, mapName).Put(mapName, value);

            public static BigInteger Get()
            {
                var value = new StorageMap(Storage.CurrentContext, mapName).Get(mapName);
                return value is null ? 0 : (BigInteger)value;
            }
        }

        #region Option Pool

            private static readonly StorageContext currentContext = Storage.CurrentContext;
            public const string rentalFeeAccumulatorMapName = "rentalFee";
            public const string RentalPriceMapName = "rentalPrice";
            public const string RentalPriceAccumulationMapName = "rentalPriceAccumulation";
            public const string TenantPriceAccumulationMapName = "tenantPriceAccumulation";
            public const string RentalPriceUpdateTimeMapName = "rentalPriceUpdateTime";
            public const string rentedToken0MapName = "rent0";
            public const string totalRentedToken0MapName = "totalRent0";
            public const string marginToken0MapName = "margin0";
            public const string rentedToken1MapName = "rent1";
            public const string totalRentedToken1MapName = "totalRent1";
            public const string marginToken1MapName = "margin1";
            public static BigInteger TenantRentedToken0(UInt160 tenant) => (BigInteger)new StorageMap(currentContext, rentedToken0MapName).Get(tenant);
            public static BigInteger TotalRentedToken0() => (BigInteger)Storage.Get(currentContext, totalRentedToken0MapName);
            private static void PutRentedToken0(UInt160 tenant, BigInteger value)
            {
                // update the total rented amount and the user's rented amount
                Storage.Put(currentContext, totalRentedToken0MapName, TotalRentedToken0() + value - TenantRentedToken0(tenant));
                if (value > 0)
                    new StorageMap(currentContext, rentedToken0MapName).Put(tenant, value);
                else
                    new StorageMap(currentContext, rentedToken0MapName).Delete(tenant);
            }
            public static BigInteger TenantRentedToken1(UInt160 tenant) => (BigInteger)new StorageMap(currentContext, rentedToken1MapName).Get(tenant);
            public static BigInteger TotalRentedToken1() => (BigInteger)Storage.Get(currentContext, totalRentedToken1MapName);
            private static void PutRentedToken1(UInt160 tenant, BigInteger value)
            {
                // update the total rented amount and the user's rented amount
                Storage.Put(currentContext, totalRentedToken1MapName, TotalRentedToken1() + value - TenantRentedToken1(tenant));
                if (value > 0)
                    new StorageMap(currentContext, rentedToken1MapName).Put(tenant, value);
                else
                    new StorageMap(currentContext, rentedToken1MapName).Delete(tenant);
            }

            public static BigInteger TenantRentedLiquidity(UInt160 tenant) => (TenantRentedToken0(tenant) * TenantRentedToken1(tenant)).Sqrt();
            public static BigInteger TotalRentedLiquidity() => (TotalRentedToken0() * TotalRentedToken1()).Sqrt();

            public static BigInteger UtilizationRate()
            {
                // theoretically the returned value should be between 0 and 1
                // but we return 1000x theoretical value
                // TODO: we are using all BigInteger here. Ensure accuracy with float-like computation using BigInteger
                Iterator tenantRentedValue = Storage.Find(currentContext, rentedToken0MapName, FindOptions.RemovePrefix);
                UInt160 tenant;
                BigInteger tenantRentedValue0;
                BigInteger tenantRentedValue1;
                BigInteger sumLiquidity = 0;
                while (tenantRentedValue.Next())
                {
                    object[] objectArray = (object[])tenantRentedValue.Value;
                    tenant = (UInt160)objectArray[0];
                    tenantRentedValue0 = (BigInteger)objectArray[1];
                    tenantRentedValue1 = TenantRentedToken1(tenant);
                    sumLiquidity += (tenantRentedValue0 * tenantRentedValue1 * 4294967296).Sqrt();  //4294967296 == 65536 ** 2
                }
                var r = ReservePair;
                return sumLiquidity * 1000 / (65536 * (r.Reserve0 * r.Reserve1).Sqrt());
            }

            public static BigInteger RentalPrice(bool getUpperBound = false)
            {
                // GAS (1e8) cost per second
                // TODO: design a good function
                // return 0;  // for test
                BigInteger utilizationRate;
                if (getUpperBound)
                    utilizationRate = 1000;
                else
                    utilizationRate = UtilizationRate();  // utilizationRate range: [0,1000]
                return utilizationRate * utilizationRate;
            }

            public static BigInteger[] RentalFeeAccumulator()
            {
                BigInteger previousOptionPrice = (BigInteger)Storage.Get(currentContext, RentalPriceMapName);
                BigInteger previousOptionPriceAccumulation = (BigInteger)Storage.Get(currentContext, RentalPriceAccumulationMapName);
                BigInteger previousOptionPriceUpdatedTime = (BigInteger)Storage.Get(currentContext, RentalPriceUpdateTimeMapName);
                BigInteger currentTime = Runtime.Time;
                return new BigInteger[] {currentTime, previousOptionPriceAccumulation + previousOptionPrice * (currentTime - previousOptionPriceUpdatedTime) };
            }

            private static bool SettleTenantRentalFee(UInt160 tenant)
            {
                // This method is called when any tenant's rented amount of liquidity pool is going to be changed
                // (or you can also call it at any time wasting GAS...)
                // calculate how much rental fee the tenant should pay since the tenant's last settlement
                // update tenantPriceAccumulationMap, deduce tenant's fee and return true if the tenant's margin is enough to pay the rental fee
                // force liquidation and return false if the tenant's margin is not enough to pay the rental fee
                BigInteger previousOptionPrice = (BigInteger)Storage.Get(currentContext, RentalPriceMapName);
                BigInteger previousOptionPriceAccumulation = (BigInteger)Storage.Get(currentContext, RentalPriceAccumulationMapName);
                BigInteger previousOptionPriceUpdatedTime = (BigInteger)Storage.Get(currentContext, RentalPriceUpdateTimeMapName);
                BigInteger currentTime = Runtime.Time;

                BigInteger newPriceAccumulation = previousOptionPriceAccumulation + previousOptionPrice * (currentTime - previousOptionPriceUpdatedTime);

                StorageMap tenantPriceAccumulationMap = new(currentContext, TenantPriceAccumulationMapName);
                BigInteger tenantPreviousPriceAccumulation = (BigInteger)tenantPriceAccumulationMap.Get(tenant);
                BigInteger tenantRentedLiquidity = TenantRentedLiquidity(tenant);
                BigInteger tenantShouldPay = (newPriceAccumulation - tenantPreviousPriceAccumulation) * tenantRentedLiquidity;

                BigInteger tenantMarginToken0 = TenantMarginToken0(tenant);
                BigInteger tenantMarginToken1 = TenantMarginToken1(tenant);
                BigInteger tenantTotalMargin = (tenantMarginToken0 * tenantMarginToken1).Sqrt();
                if (tenantShouldPay + tenantRentedLiquidity < tenantTotalMargin)
                {
                    // normal payment
                    tenantPriceAccumulationMap.Put(tenant, newPriceAccumulation);
                    BigInteger newTenantTotalMargin = tenantTotalMargin - tenantShouldPay;
                    PutTenantMarginToken0(tenant, tenantMarginToken0 * newTenantTotalMargin / tenantTotalMargin);
                    PutTenantMarginToken1(tenant, tenantMarginToken1 * newTenantTotalMargin / tenantTotalMargin);
                    return true;
                }
                else
                {
                    // force liquidation
                    PutTenantMarginToken0(tenant, 0);
                    PutTenantMarginToken1(tenant, 0);
                    PutRentedToken0(tenant, 0);
                    PutRentedToken1(tenant, 0);

                    // Storage.Put(currentContext, OptionPriceMapName, OptionPrice());  // do this after changing tenant's rented value
                    Storage.Put(currentContext, RentalPriceAccumulationMapName, newPriceAccumulation);
                    Storage.Put(currentContext, RentalPriceUpdateTimeMapName, currentTime);

                    SafeTransfer(Token0, Runtime.ExecutingScriptHash, tenant, tenantMarginToken0);
                    SafeTransfer(Token1, Runtime.ExecutingScriptHash, tenant, tenantMarginToken1);
                    return false;
                }

            }

            public static BigInteger TenantMarginToken0(UInt160 tenant) => (BigInteger)new StorageMap(currentContext, marginToken0MapName).Get(tenant);
            private static void PutTenantMarginToken0(UInt160 tenant, BigInteger value)
            {
                if(value > 0)
                    new StorageMap(currentContext, marginToken0MapName).Put(tenant, value);
                else
                    new StorageMap(currentContext, marginToken0MapName).Delete(tenant);
            }
            public static BigInteger TenantMarginToken1(UInt160 tenant) => (BigInteger)new StorageMap(currentContext, marginToken1MapName).Get(tenant);
            private static void PutTenantMarginToken1(UInt160 tenant, BigInteger value)
            {
                if (value <= 0)
                    new StorageMap(currentContext, marginToken1MapName).Delete(tenant);
                else
                    new StorageMap(currentContext, marginToken1MapName).Put(tenant, value);
            }
            public static BigInteger TenantTotalMargin(UInt160 tenant) => (TenantMarginToken0(tenant)* TenantMarginToken1(tenant)).Sqrt();

            public static BigInteger RentOptionPool(UInt160 tenant,
                BigInteger rentToken0, BigInteger rentToken1,
                BigInteger marginToken0, BigInteger marginToken1)
            {
                Assert(EnteredStorage.Get() == 0, "Re-entered");
                EnteredStorage.Put(1);

                //Assert(Runtime.CheckWitness(tenant), "No witness");  // not necessary. We will transfer token from tenant later

                SettleTenantRentalFee(tenant);  // SafeTransfer to user inside. Be cautious of re-entrancy attack

                var r = ReservePair;
                var reserve0 = r.Reserve0;
                var reserve1 = r.Reserve1;
                BigInteger returnedValue;
                if (rentToken0 > 0 && rentToken1 == 0)
                {
                    rentToken1 = rentToken0 * reserve1 / reserve0;
                    returnedValue = rentToken1;
                }
                else if (rentToken1 > 0 && rentToken0 == 0)
                {
                    rentToken0 = rentToken1 * reserve0 / reserve1;
                    returnedValue = rentToken0;
                }
                else return 0;
                Assert(TotalRentedToken0() + rentToken0 <= reserve0 && TotalRentedToken1() + rentToken1 <= reserve1, "No enough reserve");
                BigInteger willHaveMargin0 = TenantMarginToken0(tenant) + marginToken0;
                BigInteger willHaveMargin1 = TenantMarginToken1(tenant) + marginToken1;
                BigInteger willRentToken0 = TenantRentedToken0(tenant) + rentToken0;
                BigInteger willRentToken1 = TenantRentedToken1(tenant) + rentToken1;

                Assert(willHaveMargin0 * willHaveMargin1 > willRentToken0 * willRentToken1, "No enough margin");

                SafeTransfer(Token0, tenant, Runtime.ExecutingScriptHash, marginToken0);
                SafeTransfer(Token1, tenant, Runtime.ExecutingScriptHash, marginToken1);
                PutTenantMarginToken0(tenant, willHaveMargin0);
                PutTenantMarginToken1(tenant, willHaveMargin1);
                PutRentedToken0(tenant, willRentToken0);
                PutRentedToken1(tenant, willRentToken1);
                Storage.Put(currentContext, RentalPriceMapName, RentalPrice());

                EnteredStorage.Put(0);
                return returnedValue;
            }

            public static void AddMargin(UInt160 tenant, BigInteger marginToken0, BigInteger marginToken1)
            {
                Assert(EnteredStorage.Get() == 0, "Re-entered");
                EnteredStorage.Put(1);

                //Assert(Runtime.CheckWitness(tenant), "No witness");  // not necessary. We will transfer token from tenant later

                SafeTransfer(Token0, tenant, Runtime.ExecutingScriptHash, marginToken0);
                SafeTransfer(Token1, tenant, Runtime.ExecutingScriptHash, marginToken1);
                PutTenantMarginToken0(tenant, TenantMarginToken0(tenant) + marginToken0);
                PutTenantMarginToken0(tenant, TenantMarginToken1(tenant) + marginToken1);

                EnteredStorage.Put(0);
            }
            public static void WithdrawMargin(UInt160 tenant)
            {
                Assert(EnteredStorage.Get() == 0, "Re-entered");
                EnteredStorage.Put(1);

                if (SettleTenantRentalFee(tenant))
                {
                    Assert(Runtime.CheckWitness(tenant), "No witness");  // anyone can force another's liquidation
                    BigInteger currentMargin0 = TenantMarginToken0(tenant);
                    BigInteger currentMargin1 = TenantMarginToken1(tenant);
                    PutTenantMarginToken0(tenant, 0);
                    PutTenantMarginToken1(tenant, 0);
                    PutRentedToken0(tenant, 0);
                    PutRentedToken1(tenant, 0);
                    SafeTransfer(Token0, Runtime.ExecutingScriptHash, tenant, currentMargin0);
                    SafeTransfer(Token1, Runtime.ExecutingScriptHash, tenant, currentMargin1);
                }
                else
                {
                    // force liquidation executed. 
                    // TODO: give the caller some rewards...
                }

                Storage.Put(currentContext, RentalPriceMapName, RentalPrice());

                EnteredStorage.Put(0);
            }

            public static bool SwapInOptionPool(UInt160 tenant, BigInteger amount0Out, BigInteger amount1Out, UInt160 toAddress, byte[] data = null)
            {
                Assert(EnteredStorage.Get() == 0, "Re-entered");
                EnteredStorage.Put(1);

                if (!SettleTenantRentalFee(tenant)) return false;

                Assert(toAddress.IsAddress(), "Invalid To-Address");
                var caller = Runtime.CallingScriptHash;

                var me = Runtime.ExecutingScriptHash;

                Assert(amount0Out >= 0 && amount1Out >= 0, "Invalid AmountOut");
                Assert(amount0Out > 0 || amount1Out > 0, "Invalid AmountOut");

                var r = ReservePair;
                var reserve0 = TenantRentedToken0(tenant);
                var reserve1 = TenantRentedToken1(tenant);

                //转出量小于持有量
                Assert(amount0Out < reserve0 && amount1Out < reserve1, "Insufficient Liquidity");
                //禁止转到token本身的地址
                Assert(toAddress != Token0 && toAddress != Token1 && toAddress != me, "INVALID_TO");

                if (amount0Out > 0)
                {
                    PutRentedToken0(tenant, TenantRentedToken0(tenant) - amount0Out);
                    //从本合约转出目标token到目标地址
                    SafeTransfer(Token0, me, toAddress, amount0Out, data);
                }
                if (amount1Out > 0)
                {
                    PutRentedToken1(tenant, TenantRentedToken1(tenant) - amount1Out);
                    SafeTransfer(Token1, me, toAddress, amount1Out, data);
                }


                BigInteger balance0 = DynamicBalanceOf(Token0, me);
                BigInteger balance1 = DynamicBalanceOf(Token1, me);
                //计算转入的token量：转入转出后token余额balance>reserve，代表token转入，计算结果为正数
                var amount0In = balance0 > (reserve0 - amount0Out) ? balance0 - (reserve0 - amount0Out) : 0;
                var amount1In = balance1 > (reserve1 - amount1Out) ? balance1 - (reserve1 - amount1Out) : 0;
                //swap 时至少有一个转入
                Assert(amount0In > 0 || amount1In > 0, "Invalid AmountIn");

                //amountIn 收取千分之三手续费
                var balance0Adjusted = balance0 * 1000 - amount0In * 3;
                var balance1Adjusted = balance1 * 1000 - amount1In * 3;

                //恒定积
                Assert(balance0Adjusted * balance1Adjusted >= reserve0 * reserve1 * 1_000_000, "K");

                Update(balance0, balance1, r);

                Swapped(caller, amount0In, amount1In, amount0Out, amount1Out, toAddress);
                EnteredStorage.Put(0);
                return true;
            }

            public static UInt160[] FindForcedLiquidation()
            {
                Iterator tenants = new StorageMap(currentContext, rentedToken0MapName).Find(FindOptions.RemovePrefix);
                UInt160[] returnedTenants = new UInt160[500];
                int i = 0;
                while(tenants.Next() && i < 500)
                {
                    UInt160 tenant = (UInt160)((object[])tenants.Value)[0];
                    if((BigInteger)((object[])tenants.Value)[1] * TenantRentedToken1(tenant) > TenantRentedToken0(tenant) * TenantRentedToken1(tenant))
                        returnedTenants[i++] = tenant;
                }
                return returnedTenants;
            }

            public static void ForceLiquidation(UInt160 tenant)
            {
                BigInteger marginToken0 = TenantMarginToken0(tenant);
                BigInteger marginToken1 = TenantMarginToken1(tenant);
                if (TenantRentedToken0(tenant) * TenantRentedToken1(tenant) > marginToken0 * marginToken1)
                {
                    PutRentedToken0(tenant, 0);
                    PutRentedToken1(tenant, 0);
                    SafeTransfer(Token0, Runtime.ExecutingScriptHash, tenant, marginToken0);
                    SafeTransfer(Token1, Runtime.ExecutingScriptHash, tenant, marginToken1);
                }
            }

        #endregion

        #region Swap

        /// <summary>
        /// 完成兑换，amount0Out 和 amount1Out必需一个为0一个为正数
        /// </summary>
        /// <param name="amount0Out">已经计算好的token0 转出量</param>
        /// <param name="amount1Out">已经计算好的token1 转出量</param>
        /// <param name="toAddress"></param>
        public static bool Swap(BigInteger amount0Out, BigInteger amount1Out, UInt160 toAddress, byte[] data = null)
        {
            //检查是否存在reentered的情况
            Assert(EnteredStorage.Get() == 0, "Re-entered");
            EnteredStorage.Put(1);

            Assert(toAddress.IsAddress(), "Invalid To-Address");
            var caller = Runtime.CallingScriptHash;

            var me = Runtime.ExecutingScriptHash;

            Assert(amount0Out >= 0 && amount1Out >= 0, "Invalid AmountOut");
            Assert(amount0Out > 0 || amount1Out > 0, "Invalid AmountOut");

            var r = ReservePair;
            var reserve0 = r.Reserve0;
            var reserve1 = r.Reserve1;

            //转出量小于持有量
            Assert(amount0Out < reserve0 - TotalRentedToken0() && amount1Out < reserve1 - TotalRentedToken0(), "Insufficient Liquidity");
            //禁止转到token本身的地址
            Assert(toAddress != (UInt160)Token0 && toAddress != (UInt160)Token1 && toAddress != me, "INVALID_TO");

            if (amount0Out > 0)
            {
                //从本合约转出目标token到目标地址
                SafeTransfer(Token0, me, toAddress, amount0Out, data);
            }
            if (amount1Out > 0)
            {
                SafeTransfer(Token1, me, toAddress, amount1Out, data);
            }


            BigInteger balance0 = DynamicBalanceOf(Token0, me);
            BigInteger balance1 = DynamicBalanceOf(Token1, me);
            //计算转入的token量：转入转出后token余额balance>reserve，代表token转入，计算结果为正数
            var amount0In = balance0 > (reserve0 - amount0Out) ? balance0 - (reserve0 - amount0Out) : 0;
            var amount1In = balance1 > (reserve1 - amount1Out) ? balance1 - (reserve1 - amount1Out) : 0;
            //swap 时至少有一个转入
            Assert(amount0In > 0 || amount1In > 0, "Invalid AmountIn");

            //amountIn 收取千分之三手续费
            var balance0Adjusted = balance0 * 1000 - amount0In * 3;
            var balance1Adjusted = balance1 * 1000 - amount1In * 3;

            //恒定积
            Assert(balance0Adjusted * balance1Adjusted >= reserve0 * reserve1 * 1_000_000, "K");

            Update(balance0, balance1, r);

            Swapped(caller, amount0In, amount1In, amount0Out, amount1Out, toAddress);
            EnteredStorage.Put(0);
            return true;
        }


        #endregion

        #region Burn and Mint

        /// <summary>
        /// 销毁liquidity代币，并转出等量的token0和token1到toAddress
        /// 需要事先将用户持有的liquidity转入本合约才可以调此方法
        /// </summary>
        /// <param name="toAddress"></param>
        /// <returns></returns>
        public static object Burn(UInt160 toAddress)
        {
            //检查是否存在reentered的情况
            Assert(EnteredStorage.Get() == 0, "Re-entered");
            EnteredStorage.Put(1);
            Assert(toAddress.IsAddress(), "Invalid To-Address");

            var caller = Runtime.CallingScriptHash;
            //Assert(CheckIsRouter(caller), "Only Router Can Burn");
            var me = Runtime.ExecutingScriptHash;
            var r = ReservePair;

            var balance0 = DynamicBalanceOf(Token0, me);
            var balance1 = DynamicBalanceOf(Token1, me);
            var liquidity = BalanceOf(me);

            var totalSupply = TotalSupply();
            var amount0 = liquidity * balance0 / totalSupply;//要销毁(转出)的token0额度：me持有的token0 * (me持有的me token/me token总量）
            var amount1 = liquidity * balance1 / totalSupply;

            Assert(amount0 > 0 && amount1 > 0, "Insufficient LP Burned");
            BurnToken(me, liquidity);

            //从本合约转出token
            SafeTransfer(Token0, me, toAddress, amount0);
            SafeTransfer(Token1, me, toAddress, amount1);

            balance0 = DynamicBalanceOf(Token0, me);
            balance1 = DynamicBalanceOf(Token1, me);

            Update(balance0, balance1, r);

            Burned(caller, liquidity, amount0, amount1, toAddress);

            EnteredStorage.Put(0);
            return new BigInteger[]
            {
                amount0,
                amount1,
            };
        }


        /// <summary>
        /// 铸造代币，此方法应该由router在AddLiquidity时调用
        /// </summary>
        /// <param name="toAddress"></param>
        /// <returns>返回本次铸币量</returns>
        public static BigInteger Mint(UInt160 toAddress)
        {
            //检查是否存在reentered的情况
            Assert(EnteredStorage.Get() == 0, "Re-entered");
            EnteredStorage.Put(1);
            Assert(toAddress.IsAddress(), "Invalid To-Address");

            var caller = Runtime.CallingScriptHash; //msg.sender
            //Assert(CheckIsRouter(caller), "Only Router Can Mint");

            var me = Runtime.ExecutingScriptHash; //address(this)

            var r = ReservePair;
            var reserve0 = r.Reserve0;
            var reserve1 = r.Reserve1;
            var balance0 = DynamicBalanceOf(Token0, me);
            var balance1 = DynamicBalanceOf(Token1, me);

            var amount0 = balance0 - reserve0;//token0增量
            var amount1 = balance1 - reserve1;//token1增量

            var totalSupply = TotalSupply();

            BigInteger liquidity;
            if (totalSupply == 0)
            {
                liquidity = (amount0 * amount1).Sqrt() - MINIMUM_LIQUIDITY;

                MintToken(UInt160.Zero, MINIMUM_LIQUIDITY);// permanently lock the first MINIMUM_LIQUIDITY tokens,永久锁住第一波发行的 MINIMUM_LIQUIDITY token
            }
            else
            {
                var liquidity0 = amount0 * totalSupply / reserve0;
                var liquidity1 = amount1 * totalSupply / reserve1;
                liquidity = liquidity0 > liquidity1 ? liquidity1 : liquidity0;
            }

            Assert(liquidity > 0, "Insufficient LP Minted");
            MintToken(toAddress, liquidity);

            Update(balance0, balance1, r);

            Minted(caller, amount0, amount1, liquidity);

            EnteredStorage.Put(0);
            return liquidity;
        }


        /// <summary>
        /// 铸币（不校验签名），内部方法禁止外部直接调用
        /// </summary>
        /// <param name="toAddress">接收新铸造的币的账号</param>
        /// <param name="amount">铸造量</param>
        private static void MintToken(UInt160 toAddress, BigInteger amount)
        {
            AssetStorage.Increase(toAddress, amount);
            TotalSupplyStorage.Increase(amount);
            onTransfer(null, toAddress, amount);
        }

        /// <summary>
        /// 物理销毁token（不校验签名），内部方法禁止外部直接调用
        /// </summary>
        /// <param name="fromAddress">token的持有地址</param>
        /// <param name="amount">销毁的token量</param>
        private static void BurnToken(UInt160 fromAddress, BigInteger amount)
        {
            AssetStorage.Reduce(fromAddress, amount);
            TotalSupplyStorage.Reduce(amount);
            onTransfer(fromAddress, null, amount);
        }



        #endregion


        #region SyncUpdate
        /// <summary>
        /// 更新最新持有量（reserve）、区块时间戳(blockTimestamp)
        /// </summary>
        /// <param name="balance0">最新的token0持有量</param>
        /// <param name="balance1">最新的token1持有量</param>
        /// <param name="reserve">旧的reserve数据</param>
        private static void Update(BigInteger balance0, BigInteger balance1, ReservesData reserve)
        {
            BigInteger blockTimestamp = Runtime.Time / 1000 % 4294967296;
            var priceCumulative = Cumulative;
            BigInteger timeElapsed = blockTimestamp - Cumulative.BlockTimestampLast;
            if (timeElapsed > 0 && reserve.Reserve0 != 0 && reserve.Reserve1 != 0)
            {
                priceCumulative.Price0CumulativeLast += reserve.Reserve1 * FIXED * timeElapsed / reserve.Reserve0;
                priceCumulative.Price1CumulativeLast += reserve.Reserve0 * FIXED * timeElapsed / reserve.Reserve1;
                priceCumulative.BlockTimestampLast = blockTimestamp;
                Cumulative = priceCumulative;
            }
            reserve.Reserve0 = balance0;
            reserve.Reserve1 = balance1;

            ReservePair = reserve;
            Synced(balance0, balance1);
        }

        #endregion

        #region Reserve读写



        /// <summary>
        /// Reserve读写，节约gas
        /// </summary>
        private static ReservesData ReservePair
        {
            get
            {
                var val = StorageGet(nameof(ReservePair));
                if (val is null || val.Length == 0)
                {
                    return new ReservesData() { Reserve0 = 0, Reserve1 = 0 };
                }
                var b = (ReservesData)StdLib.Deserialize(val);
                return b;
            }
            set
            {

                var val = StdLib.Serialize(value);
                StoragePut(nameof(ReservePair), val);
            }
        }

        private static PriceCumulative Cumulative
        {
            get
            {
                var val = StorageGet(nameof(Cumulative));
                if (val is null || val.Length == 0)
                {
                    return new PriceCumulative() { Price0CumulativeLast = 0, Price1CumulativeLast = 0, BlockTimestampLast = 0 };
                }
                var b = (PriceCumulative)StdLib.Deserialize(val);
                return b;
            }
            set
            {
                var val = StdLib.Serialize(value);
                StoragePut(nameof(Cumulative), val);
            }
        }

        public static object GetReserves()
        {
            return ReservePair;
        }

        #endregion
    }
}
