using System.Text;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay.Input;

public static partial class InputActionCatalog
{
    /// <summary>Dashboard hotkey bind rows - single source for ImGui + web parity.</summary>
    public static string RenderDashboardRows()
    {
        var sb = new StringBuilder();
        foreach (var action in All)
        {
            sb.Append("<div class=\"row\"><div class=\"rl\" title=\"");
            sb.Append(WebUtilityHtmlEncode(action.Hint));
            sb.Append("\">");
            sb.Append(WebUtilityHtmlEncode(action.Label));
            sb.Append("</div><span class=\"hkctl\"><span class=\"hk-display\" data-hk=\"");
            sb.Append(action.Id);
            sb.Append("\">-</span><button type=\"button\" class=\"chip\" data-hk-bind=\"");
            sb.Append(action.Id);
            sb.Append("\">Bind</button><button type=\"button\" class=\"chip\" data-hk-pad=\"");
            sb.Append(action.Id);
            sb.Append("\">Pad</button><button type=\"button\" class=\"chip\" data-hk-clear=\"");
            sb.Append(action.Id);
            sb.Append("\">Clear</button></span></div>");
        }
        return sb.ToString();
    }

    private static string WebUtilityHtmlEncode(string s)
        => System.Net.WebUtility.HtmlEncode(s);
}
