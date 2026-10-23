using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CrazyGames
{
    public class StoreModule : MonoBehaviour
    {
        public const int MaxCatalogLimit = 50;

        private CrazySDK _crazySDK;
        private Action<SdkError, PurchaseResult> _buyItemCallback;
        private Action<SdkError, CatalogPage> _listCatalogItemsCallback;
        private bool _sandbox;

        public void Init(CrazySDK crazySDK)
        {
            _crazySDK = crazySDK;
        }

        /// <summary>
        /// Toggles whether the purchases run against the sandbox (test) environment.
        /// </summary>
        public void SetSandbox(bool sandbox)
        {
            _sandbox = sandbox;
            _crazySDK.WrapSDKAction(
                () =>
                {
                    StoreSetSandboxSDK(sandbox ? 1 : 0);
                },
                () =>
                {
                    _crazySDK.DebugLog("Setting sandbox mode to " + sandbox);
                }
            );
        }

        /// <summary>
        /// Opens the payment widget, allowing the user to buy a single item.
        /// Only one purchase is allowed at a time, concurrent calls will receive an error.
        /// Callback param1 = error (null if everything is fine), param2 = purchase result.
        /// Possible error codes: "userNotAuthenticated", "purchaseInitFailed", "purchaseInProgress", "purchaseCancelled", "unexpectedError".<br/><br/>
        /// You can customize the response returned in the editor in the CrazyGamesSettings file.
        /// </summary>
        public void BuyItem(string itemId, Action<SdkError, PurchaseResult> callback)
        {
            _crazySDK.WrapSDKAction(
                () =>
                {
                    if (_buyItemCallback != null)
                    {
                        callback(new SdkError("purchaseInProgress", "A purchase is already in progress."), null);
                        return;
                    }

                    _buyItemCallback = callback;
                    StoreBuyItemSDK(itemId);
                },
                () =>
                {
                    var settings = _crazySDK.Settings;
                    _crazySDK.DebugLog(
                        $"Buy item \"{itemId}\" simulation for Unity editor (sandbox: {_sandbox}), returning: {settings.buyItemResponse}"
                    );
                    switch (settings.buyItemResponse)
                    {
                        case CrazySettingsBuyItemResponse.Success:
                            callback(null, new PurchaseResult { itemId = itemId });
                            return;
                        case CrazySettingsBuyItemResponse.PurchaseCancelled:
                            callback(new SdkError("purchaseCancelled", "The purchase was cancelled."), null);
                            return;
                        case CrazySettingsBuyItemResponse.UserLoggedOut:
                            callback(new SdkError("userNotAuthenticated", "User must be logged in to perform this action."), null);
                            return;
                        case CrazySettingsBuyItemResponse.UnexpectedError:
                            callback(new SdkError("unexpectedError", "Simulated error in editor"), null);
                            return;
                        default:
                            Debug.LogError("Unhandled case " + settings.buyItemResponse + ", returning 'unexpectedError'");
                            callback(new SdkError("unexpectedError", "Simulated error in editor"), null);
                            return;
                    }
                }
            );
        }

        /// <summary>
        /// Opens the payment widget, allowing the user to buy a single item.
        /// Only one purchase is allowed at a time, concurrent calls will throw.
        /// Possible error codes: "userNotAuthenticated", "purchaseInitFailed", "purchaseInProgress", "purchaseCancelled", "unexpectedError".<br/><br/>
        /// You can customize the response returned in the editor in the CrazyGamesSettings file.
        /// </summary>
        public System.Threading.Tasks.Task<PurchaseResult> BuyItemAsync(string itemId)
        {
            return CrazyTaskUtil.FromCallback<PurchaseResult>(cb => BuyItem(itemId, cb));
        }

        /// <summary>
        /// Lists the first page of the items of the game's catalog.
        /// Callback param1 = error (null if everything is fine), param2 = catalog page.
        /// </summary>
        public void ListCatalogItems(Action<SdkError, CatalogPage> callback)
        {
            ListCatalogItems(MaxCatalogLimit, 0, callback);
        }

        /// <summary>
        /// Lists one page of the items of the game's catalog.
        /// Only one catalog request is allowed at a time, concurrent calls will receive an error.
        /// Callback param1 = error (null if everything is fine), param2 = catalog page.
        /// Possible error codes: "catalogFetchFailed", "invalidArgument", "requestInProgress", "unexpectedError", "other".
        /// </summary>
        /// <param name="limit">How many items to return, between 1 and 50</param>
        /// <param name="offset">How many items to skip, 0 for the first page</param>
        /// <param name="callback">Callback with error (null if ok) and CatalogPage result</param>
        public void ListCatalogItems(int limit, int offset, Action<SdkError, CatalogPage> callback)
        {
            _crazySDK.WrapSDKAction(
                () =>
                {
                    if (_listCatalogItemsCallback != null)
                    {
                        callback(new SdkError("requestInProgress", "A catalog request is already in progress."), null);
                        return;
                    }

                    if (!ValidateCatalogArguments(limit, offset, callback))
                    {
                        return;
                    }

                    _listCatalogItemsCallback = callback;
                    StoreListCatalogItemsSDK(limit, offset);
                },
                () =>
                {
                    if (!ValidateCatalogArguments(limit, offset, callback))
                    {
                        return;
                    }

                    _crazySDK.DebugLog($"List catalog items simulation for Unity editor, limit: {limit}, offset: {offset}");

                    var allItems = new List<CatalogItem>
                    {
                        new CatalogItem
                        {
                            sku = "sword",
                            name = "Sword",
                            description = "A demo sword",
                            imageUrl = "https://images.crazygames.com/userportal/avatars/1.png",
                            price = new CatalogItemPrice
                            {
                                amount = "4.99",
                                amountWithoutDiscount = "4.99",
                                currency = "USD",
                            },
                            isFree = false,
                            canBeBought = true,
                        },
                        new CatalogItem
                        {
                            sku = "gem_pack_100",
                            name = "100 gems",
                            description = "A demo discounted gem pack",
                            imageUrl = "https://images.crazygames.com/userportal/avatars/2.png",
                            price = new CatalogItemPrice
                            {
                                amount = "9.99",
                                amountWithoutDiscount = "12.99",
                                currency = "USD",
                            },
                            isFree = false,
                            canBeBought = true,
                        },
                        new CatalogItem
                        {
                            sku = "starter_pack",
                            name = "Starter pack",
                            description = "A demo item without a real money price",
                            imageUrl = null,
                            price = null,
                            isFree = true,
                            canBeBought = false,
                        },
                    };

                    var start = Math.Min(Math.Max(0, offset), allItems.Count);
                    var count = Math.Min(limit, allItems.Count - start);
                    callback(null, new CatalogPage { items = allItems.GetRange(start, count), hasMore = start + count < allItems.Count });
                }
            );
        }

        /// <summary>
        /// Lists the first page of the items of the game's catalog.
        /// </summary>
        public System.Threading.Tasks.Task<CatalogPage> ListCatalogItemsAsync()
        {
            return ListCatalogItemsAsync(MaxCatalogLimit, 0);
        }

        /// <summary>
        /// Lists one page of the items of the game's catalog.
        /// Only one catalog request is allowed at a time, concurrent calls will throw.
        /// Possible error codes: "catalogFetchFailed", "invalidArgument", "requestInProgress", "unexpectedError", "other".
        /// </summary>
        /// <param name="limit">How many items to return, between 1 and 50</param>
        /// <param name="offset">How many items to skip, 0 for the first page</param>
        public System.Threading.Tasks.Task<CatalogPage> ListCatalogItemsAsync(int limit, int offset)
        {
            return CrazyTaskUtil.FromCallback<CatalogPage>(cb => ListCatalogItems(limit, offset, cb));
        }

        private bool ValidateCatalogArguments(int limit, int offset, Action<SdkError, CatalogPage> callback)
        {
            if (limit < 1 || limit > MaxCatalogLimit)
            {
                callback(new SdkError("invalidArgument", $"The limit must be a whole number between 1 and {MaxCatalogLimit}."), null);
                return false;
            }

            if (offset < 0)
            {
                callback(new SdkError("invalidArgument", "The offset must be a whole number of 0 or more."), null);
                return false;
            }

            return true;
        }

        private void JSLibCallback_StoreBuyItem(string responseStr)
        {
            var response = JsonUtility.FromJson<PurchaseCallbackReply>(responseStr);
            var callback = _buyItemCallback;
            // clear the callback before invoking it, the game may start a new purchase from the callback
            _buyItemCallback = null;
            callback?.Invoke(response.Error, response.PurchaseResult);
        }

        private void JSLibCallback_StoreListCatalogItems(string responseStr)
        {
            var response = JsonUtility.FromJson<CatalogCallbackReply>(responseStr);
            var callback = _listCatalogItemsCallback;
            // clear the callback before invoking it, the game may request another page from the callback
            _listCatalogItemsCallback = null;
            callback?.Invoke(response.Error, response.CatalogPage);
        }

#if UNITY_WEBGL
        [DllImport("__Internal")]
        private static extern void StoreBuyItemSDK(string itemId);

        [DllImport("__Internal")]
        private static extern void StoreSetSandboxSDK(int sandbox);

        [DllImport("__Internal")]
        private static extern void StoreListCatalogItemsSDK(int limit, int offset);
#else
        // Preventing build to fail when using another platform than WebGL
        private void StoreBuyItemSDK(string itemId) { }

        private void StoreSetSandboxSDK(int sandbox) { }

        private void StoreListCatalogItemsSDK(int limit, int offset) { }
#endif

        [Serializable]
        private class PurchaseCallbackReply
        {
            public string errorJson; // JsonUtility initializes null class with empty fields, so pass them as JSON strings and parse them here
            public string purchaseResultJson;

            public SdkError Error =>
                string.IsNullOrEmpty(errorJson) || errorJson == "null" || errorJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<SdkError>(errorJson);

            public PurchaseResult PurchaseResult =>
                string.IsNullOrEmpty(purchaseResultJson) || purchaseResultJson == "null" || purchaseResultJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<PurchaseResult>(purchaseResultJson);
        }

        [Serializable]
        private class CatalogCallbackReply
        {
            public string errorJson; // JsonUtility initializes null class with empty fields, so pass them as JSON strings and parse them here
            public string catalogPageJson;

            public SdkError Error =>
                string.IsNullOrEmpty(errorJson) || errorJson == "null" || errorJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<SdkError>(errorJson);

            public CatalogPage CatalogPage =>
                string.IsNullOrEmpty(catalogPageJson) || catalogPageJson == "null" || catalogPageJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<CatalogPage>(catalogPageJson);
        }
    }

    [Serializable]
    public class PurchaseResult
    {
        public string itemId;

        public override string ToString()
        {
            return base.ToString() + "ItemId = " + itemId;
        }
    }

    [Serializable]
    public class CatalogItemPrice
    {
        public string amount;

        public string amountWithoutDiscount;

        public string currency;

        public override string ToString()
        {
            return base.ToString() + $"Amount = {amount}, AmountWithoutDiscount = {amountWithoutDiscount}, Currency = {currency}";
        }
    }

    [Serializable]
    public class CatalogItem
    {
        public string sku;
        public string name;
        public string description;
        public string imageUrl;

        public CatalogItemPrice price;

        public bool isFree;

        public bool canBeBought;

        public bool HasPrice => price != null && !string.IsNullOrEmpty(price.currency);

        public override string ToString()
        {
            return base.ToString()
                + $"Sku = {sku}, Name = {name}, Description = {description}, ImageUrl = {imageUrl}, "
                + $"Price = {(HasPrice ? price.ToString() : "none")}, IsFree = {isFree}, CanBeBought = {canBeBought}";
        }
    }

    [Serializable]
    public class CatalogPage
    {
        public List<CatalogItem> items;

        public bool hasMore;
    }
}
