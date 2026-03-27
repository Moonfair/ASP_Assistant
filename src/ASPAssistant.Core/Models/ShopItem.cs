namespace ASPAssistant.Core.Models;

public class ShopItem
{
    public string Name { get; set; } = "";
    public int Price { get; set; }
    public bool IsTracked { get; set; }
    public (double X, double Y, double Width, double Height) OcrRegion { get; set; }
}
