using System;
using Game.Info;
using Game.UI;
using Game.UI.Windows.Windows;
using ScriptableObjectScripts;

namespace AiPlayerIntel.Intel;

static class IntelActions {
    internal static void OpenMarket(ObjectInfo? body) {
        if (body == null) { return; }
        var uiManager = SerializedMonoBehaviourSingleton<UIManager>.Instance;
        if (uiManager == null) { return; }
        uiManager.Open(EWindowType.ObjectInfo, body);
        uiManager.Open(EWindowType.MarketOffer, body);
    }

    // Opens the make-offer dialog pre-filled with a sell offer at the AI's max-buy ceiling.
    // No auto-submit: the user reviews and clicks the dialog's confirm button.
    internal static void OpenOffer(ObjectInfo body, ResourceDefinition resourceDefinition, double maxBuyPerUnit, double needQuantity) {
        var uiManager = SerializedMonoBehaviourSingleton<UIManager>.Instance;
        if (uiManager == null) { return; }
        uiManager.Open(EWindowType.MarketOfferMakeOffer, body);
        var dialog = uiManager.GetWindow<MarketOfferMakeOfferWindow>();
        if (dialog == null) {
            OpenMarket(body);
            return;
        }
        dialog.sell.isOn = true;
        dialog.rdDropDown.SetSelectRD(resourceDefinition);
        var price = Math.Floor(maxBuyPerUnit * 100.0) / 100.0;
        dialog.pricePerUnitInputField.text = price.ToString();
        dialog.countToBuySellSlider.value = (float)needQuantity;
    }
}
