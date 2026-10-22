using PowerSwitcher.Core;

DisplayManager displayManager = new DisplayManager();
var displays = displayManager.GetDisplays();
var f = displayManager.GetSupportedRefreshRates(displays[0].DeviceName);
foreach (var item in f)
{
    Console.WriteLine(item);
}
