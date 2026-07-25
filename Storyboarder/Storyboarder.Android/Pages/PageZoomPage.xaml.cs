namespace Storyboarder.Android.Pages;

// A single composed page, full-screen, with pinch-to-zoom, pan while zoomed, and double-tap to reset —
// the "panel zoom" of the reader. Constructed with the page image's on-disk path.
public partial class PageZoomPage : ContentPage
{
    private double currentScale = 1;
    private double startScale = 1;
    private double xOffset;
    private double yOffset;

    public PageZoomPage(string imagePath)
    {
        InitializeComponent();
        PageImage.Source = ImageSource.FromFile(imagePath);
    }

    private void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            startScale = PageImage.Scale;
            PageImage.AnchorX = 0;
            PageImage.AnchorY = 0;
        }
        else if (e.Status == GestureStatus.Running)
        {
            currentScale += (e.Scale - 1) * startScale;
            currentScale = Math.Clamp(currentScale, 1, 5);

            // Keep the point under the fingers fixed as the image scales (the MAUI pinch pattern).
            var renderedX = PageImage.X + xOffset;
            var deltaX = renderedX / Width;
            var deltaWidth = Width / (PageImage.Width * startScale);
            var originX = (e.ScaleOrigin.X - deltaX) * deltaWidth;

            var renderedY = PageImage.Y + yOffset;
            var deltaY = renderedY / Height;
            var deltaHeight = Height / (PageImage.Height * startScale);
            var originY = (e.ScaleOrigin.Y - deltaY) * deltaHeight;

            var targetX = xOffset - (originX * PageImage.Width) * (currentScale - startScale);
            var targetY = yOffset - (originY * PageImage.Height) * (currentScale - startScale);

            PageImage.TranslationX = Math.Clamp(targetX, -PageImage.Width * (currentScale - 1), 0);
            PageImage.TranslationY = Math.Clamp(targetY, -PageImage.Height * (currentScale - 1), 0);
            PageImage.Scale = currentScale;
        }
        else if (e.Status == GestureStatus.Completed)
        {
            xOffset = PageImage.TranslationX;
            yOffset = PageImage.TranslationY;
        }
    }

    private void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        // Panning only makes sense once zoomed in.
        if (currentScale <= 1)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Running:
                PageImage.TranslationX = Math.Clamp(xOffset + e.TotalX, -PageImage.Width * (currentScale - 1), 0);
                PageImage.TranslationY = Math.Clamp(yOffset + e.TotalY, -PageImage.Height * (currentScale - 1), 0);
                break;
            case GestureStatus.Completed:
                xOffset = PageImage.TranslationX;
                yOffset = PageImage.TranslationY;
                break;
        }
    }

    private void OnDoubleTap(object? sender, TappedEventArgs e)
    {
        currentScale = 1;
        startScale = 1;
        xOffset = 0;
        yOffset = 0;
        PageImage.Scale = 1;
        PageImage.TranslationX = 0;
        PageImage.TranslationY = 0;
    }
}
