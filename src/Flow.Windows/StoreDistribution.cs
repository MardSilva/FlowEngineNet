namespace Flow.Windows;

internal static class StoreDistribution
{
    internal const string ProductUri = "ms-windows-store://pdp/?ProductId=9NMJW9XHMJ0F";

    internal static bool IsStorePackage
    {
        get
        {
            try
            {
                return global::Windows.ApplicationModel.Package.Current.Id.FamilyName ==
                    "ESSoftwares.FlowEngine_nxa5g9xs5gbp2";
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
