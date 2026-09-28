namespace DeckForge.App.Services;

/// <summary>
/// Wraps an SVG document in the minimal HTML a WebView2 preview needs.
/// </summary>
/// <remarks>
/// Two pages preview generated markup this way - the icon studio and the widget designer - and both
/// used to carry their own copy of this string. They had drifted: each inlined the document, and the
/// icon studio passed a bare list of shapes with no <c>&lt;svg&gt;</c> element around it, so the
/// browser had no viewport to draw into and the preview was blank. One helper, and the caller supplies
/// a document that already has its root element.
/// </remarks>
internal static class PreviewHtml
{
    /// <summary>
    /// A transparent, margin-free page that scales the document to fill its tile.
    /// </summary>
    /// <param name="document">A complete SVG (or HTML) document, root element included.</param>
    public static string Wrap(string document) =>
        "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\" />\n<style>\n" +
        "  html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; }\n" +
        "  svg { width: 100%; height: 100%; display: block; }\n" +
        "</style>\n</head>\n<body>" + document + "</body>\n</html>";
}
