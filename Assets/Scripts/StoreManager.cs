using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

public class StoreManager : MonoBehaviour, IStoreListener
{
    // Setup variables
    private static IStoreController _storeController;
    private static IExtensionProvider _extensionProvider;

    // Product IDs
    public const string productIDConsumable_MediumPotion = "health_potion_medium";
    public const string productIDNonConsumable_RareHelm = "gold_helm_rare";
    public const string productIDSubscription_AutoRenew = "monthly_access_auto_renew";

    // Computed properties
    public bool isInitialized 
    {
        get { return _storeController != null && 
                     _extensionProvider != null; }
    }

    // Start is called before the first frame update
    void Start()
    {
        InitializeIAP();
    }

    #region Public IAP methods -> UI accessible
    public void BuyConsumableItem()
    {
        PurchaseItem(productIDConsumable_MediumPotion);
    }

    public void BuyNonConsumableItem()
    {
        PurchaseItem(productIDNonConsumable_RareHelm);
    }

    public void RestorePurchases()
    {
        RestorePurchasedItems();
    }

    public void BuySubscription()
    {
        PurchaseItem(productIDSubscription_AutoRenew);
    }
    #endregion

    #region Private IAP methods
    private void InitializeIAP()
    {
        if(isInitialized)
            return;

        var purchasingModule = StandardPurchasingModule.Instance();
        purchasingModule.useFakeStoreUIMode = FakeStoreUIMode.DeveloperUser;

        var builder = ConfigurationBuilder.Instance(purchasingModule);

        builder.AddProduct(productIDConsumable_MediumPotion, ProductType.Consumable);
        builder.AddProduct(productIDNonConsumable_RareHelm, ProductType.NonConsumable, null, new PayoutDefinition(PayoutType.Item, "nothing", 1, "payout data"));

        builder.AddProduct(productIDSubscription_AutoRenew, ProductType.Subscription, new IDs
        {
            {"com.merchantmercenaries.subscription.auto", MacAppStore.Name},
            {"come.merchantmercenaries.subscription.automatic", GooglePlay.Name}
        });

        UnityPurchasing.Initialize(this, builder);
    }

    private void PurchaseItem(string productID)
    {
        if(!isInitialized)
        {
            Debug.Log("Product purchase failed, IAP not initialized...");
            return;
        }

        Product currentProduct = _storeController.products.WithID(productID);
        if(currentProduct != null && currentProduct.availableToPurchase)
        {
            _storeController.InitiatePurchase(currentProduct);
            Debug.LogFormat("Attempting to purchase item {0} asynchronously", currentProduct.definition.id);
        }
        else 
        {
           Debug.LogFormat("Attempt to purchase item {0} failed - item was not found or unavailable...", currentProduct.definition.id);
        }
    }

    private void RestorePurchasedItems()
    {
        if(!isInitialized)
        {
            Debug.Log("Purchase restoration failed, IAP not initialized!");
            return;
        }

        if(Application.platform == RuntimePlatform.IPhonePlayer || 
           Application.platform == RuntimePlatform.OSXPlayer || 
           Application.platform == RuntimePlatform.tvOS)
        {
            var appleExtension = _extensionProvider.GetExtension<IAppleExtensions>();
            appleExtension.RestoreTransactions((restoreResult) => {
                Debug.LogFormat("Purchase restoration processing: {0}. If ProcessPurchase doesn't fire there are no products to restore.", restoreResult);
            });
        }
        else if(Application.platform == RuntimePlatform.Android ||Application.platform == RuntimePlatform.WindowsPlayer)
        {
            Debug.Log("Purchases have been restored automatically, and ProcessPurchase will fire for every restored item found!");
        }
        else
        {
            Debug.LogFormat("Purchase restoration not supported on {0} platform", Application.platform);
        }
    }

    private void QuerySubscriptionInfo(Product product)
    {
        Dictionary<string, string> intro_price_dict = _extensionProvider.GetExtension<IAppleExtensions>().GetIntroductoryPriceDictionary();

        if (product.receipt != null)
        {
            Debug.Log(product.receipt);

            if (product.definition.type == ProductType.Subscription)
            {
                string price_json = (intro_price_dict == null || !intro_price_dict.ContainsKey(product.definition.storeSpecificId)) ? null : intro_price_dict[product.definition.storeSpecificId];

                SubscriptionManager p = new SubscriptionManager(product, price_json);
                SubscriptionInfo info = p.getSubscriptionInfo();

                Debug.LogFormat("{0}, {1}, {2}", info.getPurchaseDate(), info.getExpireDate(), info.isAutoRenewing());
            }
            else
            {
                Debug.Log("This product is not a subscription...");
            }
        }
        else
        {
            Debug.Log("This product does not have a valid receipt...");
        }
    }

    private bool ValidateReceipt(Product product)
    {
        bool validReceipt = true;

#if UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE_OSX
        var validator = new CrossPlatformValidator(AppleTangle.Data(), GooglePlayTangle.Data(), Application.identifier);

        try
        {
            var result = validator.Validate(product.receipt);
            foreach (IPurchaseReceipt productReceipt in result)
            {
                Debug.LogFormat("{0}, {1}, {2}", productReceipt.productID, 
                                                 productReceipt.purchaseDate, 
                                                 productReceipt.transactionID);

                AppleInAppPurchaseReceipt aReceipt = productReceipt as AppleInAppPurchaseReceipt;
                if (aReceipt != null)
                {
                    Debug.LogFormat("{0}, {1}, {2}, {3}", aReceipt.originalTransactionIdentifier,
                                                          aReceipt.subscriptionExpirationDate,
                                                          aReceipt.cancellationDate,
                                                          aReceipt.quantity);
                }

                GooglePlayReceipt gReceipt = productReceipt as GooglePlayReceipt;
                if (gReceipt != null)
                {
                    Debug.LogFormat("{0}, {1}, {2}", gReceipt.transactionID, 
                                                     gReceipt.purchaseState, 
                                                     gReceipt.purchaseToken);
                }
            }
        }
        catch (MissingStoreSecretException)
        {
            Debug.Log("You haven't supplied a secret key for this platform...");
            validReceipt = false;
        }
        catch (IAPSecurityException)
        {
            Debug.LogFormat("Invalid receipt {0}", product.receipt);
            validReceipt = false;
        }
#endif

        return validReceipt;
    }
    #endregion

    #region IStoreListener methods
    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        _storeController = controller;
        _extensionProvider = extensions;

        Debug.Log("IAP initialized!");

        _extensionProvider.GetExtension<IAppleExtensions>().RegisterPurchaseDeferredListener((item) => {
            Debug.LogFormat("Transaction deferred for {0}", item.definition.id);
        });

        foreach (var product in controller.products.all) 
        {
            Debug.Log (product.metadata.localizedTitle);
            Debug.Log (product.metadata.localizedDescription);
            Debug.Log (product.metadata.localizedPriceString);
        }
    }

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        Debug.Log("Purchasing failed to initialize...");
        switch (error)
        {
            case InitializationFailureReason.AppNotKnown:
                Debug.LogError("Check your App is correctly configured on your publishing platform...");
                break;
            case InitializationFailureReason.PurchasingUnavailable:
                Debug.Log("Purchasing service is unavailable...");
                break;
            case InitializationFailureReason.NoProductsAvailable:
                Debug.Log("No products are available for purchase...");
                break;
        }
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        string productID = args.purchasedProduct.definition.id;

        PayoutDefinition payout = args.purchasedProduct.definition.payout;
        if (payout != null)
            Debug.Log("Payout for this item detected...");

        bool validReceipt = ValidateReceipt(args.purchasedProduct);

        switch (productID)
        {
            case productIDConsumable_MediumPotion:
                Debug.LogFormat("Consumable product {0} successfully purchased!", productID);
                break;
            case productIDNonConsumable_RareHelm:
                Debug.LogFormat("Non-Consumable product {0} successfully purchased!", productID);
                break;
            case productIDSubscription_AutoRenew:
                Debug.LogFormat("Subscription {0} successfully purchased!", productID);
                QuerySubscriptionInfo(args.purchasedProduct);
                break;
            default:
                Debug.LogFormat("Product purchase failed due to unrecognized product ID -> {0}", productID);
                break;
        }

        return PurchaseProcessingResult.Complete;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        Debug.LogFormat(string.Format("Product {0} purchase failed...", product.definition.storeSpecificId));
        switch(failureReason)
        {
            case PurchaseFailureReason.PaymentDeclined:
                Debug.Log("Your payment was declined...");
                break;
            case PurchaseFailureReason.ProductUnavailable:
                Debug.Log("That product is no longer available...");
                break;
            case PurchaseFailureReason.Unknown:
                Debug.Log("An unknown problem occurred with your purchase...");
                break;
        }
    }
    #endregion
}
