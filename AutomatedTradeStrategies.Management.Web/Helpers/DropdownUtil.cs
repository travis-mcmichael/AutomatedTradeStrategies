using Microsoft.AspNetCore.Mvc.Rendering;

namespace AutomatedTradeStrategies.Management.Web.Helpers;

public static class DropdownUtil
{
    public static SelectListItem[] AnyYesNo
    {
        get
        {
            return
            [
                new SelectListItem { Value = "", Text = "ALL" },
                new SelectListItem { Value = "true", Text = "YES" },
                new SelectListItem { Value = "false", Text = "NO" }
            ];
        }
    }
}