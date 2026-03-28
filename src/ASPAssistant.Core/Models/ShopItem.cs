namespace ASPAssistant.Core.Models;

public class ShopItem
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Unique key for this card instance, used to identify it across the
    /// stabilizer and overlay pool.  Defaults to <see cref="Name"/> for
    /// backwards-compatible code paths; set to <c>"Name@CardX"</c> by the
    /// scanner to distinguish multiple cards with the same operator name.
    /// </summary>
    public string Id { get; set; } = "";

    public int Price { get; set; }
    public bool IsTracked { get; set; }
    public (double X, double Y, double Width, double Height) OcrRegion { get; set; }
}
