#if STORE
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources.Core;

internal static class StoreIconChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var context = ResourceContext.GetForViewIndependentUse();
        var resources = ResourceManager.Current.MainResourceMap;
        foreach (string form in new[] { "unplated", "lightunplated" })
        foreach (int size in new[] { 16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 128, 256 })
        {
            context.QualifierValues["AlternateForm"] = form;
            context.QualifierValues["TargetSize"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var candidate = resources.GetValue("Files/Assets/Square44x44Logo.png", context);
            string expected = $"Square44x44Logo.targetsize-{size}_altform-{form}.png";
            check(candidate.ValueAsString.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
                $"Windows resolves {size}px {form} taskbar asset");
            string path = System.IO.Path.Combine(Package.Current.InstalledLocation.Path, "Assets", expected);
            using var bitmap = new System.Drawing.Bitmap(path);
            check(bitmap.Width == size && bitmap.Height == size && bitmap.GetPixel(0, 0).A == 0,
                $"{size}px {form} taskbar asset has native dimensions and transparent corners");
        }
    }
}
#endif
