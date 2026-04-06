param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

Add-Type -AssemblyName System.Drawing

$brandDir = Join-Path $Root "BrassLedger.Web\wwwroot\branding"
$webAssetDir = Join-Path $Root "BrassLedger.Web\Assets"
$msixAssetDir = Join-Path $Root "packaging\windows\msix\Assets"

New-Item -ItemType Directory -Force -Path $brandDir, $webAssetDir, $msixAssetDir | Out-Null

$tealDark = [System.Drawing.Color]::FromArgb(255, 15, 63, 71)
$tealLight = [System.Drawing.Color]::FromArgb(255, 29, 106, 96)
$brassLight = [System.Drawing.Color]::FromArgb(255, 247, 217, 133)
$brassMid = [System.Drawing.Color]::FromArgb(255, 217, 164, 65)
$brassDark = [System.Drawing.Color]::FromArgb(255, 136, 91, 32)
$canvas = [System.Drawing.Color]::FromArgb(255, 248, 242, 231)
$paper = [System.Drawing.Color]::FromArgb(255, 244, 237, 226)

function New-BrandBitmap {
    param([int]$Width, [int]$Height)
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return @{ Bitmap = $bitmap; Graphics = $graphics }
}

function Draw-Mark {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.RectangleF]$Bounds
    )

    $Graphics.Clear($canvas)

    $pad = $Bounds.Width * 0.08
    $coinRect = [System.Drawing.RectangleF]::new($Bounds.X + $pad, $Bounds.Y + $pad, $Bounds.Width - ($pad * 2), $Bounds.Height - ($pad * 2))
    $coinBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new($coinRect.Left, $coinRect.Top),
        [System.Drawing.PointF]::new($coinRect.Right, $coinRect.Bottom),
        $brassLight,
        $brassDark)
    $blend = [System.Drawing.Drawing2D.ColorBlend]::new()
    $blend.Colors = [System.Drawing.Color[]]@($brassLight, $brassMid, $brassDark)
    $blend.Positions = [single[]]@(0, 0.5, 1)
    $coinBrush.InterpolationColors = $blend
    $Graphics.FillEllipse($coinBrush, $coinRect)

    $ringPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(150, 255, 246, 213), [Math]::Max(4, $Bounds.Width * 0.02))
    $ringRect = [System.Drawing.RectangleF]::new($coinRect.X + ($Bounds.Width * 0.05), $coinRect.Y + ($Bounds.Width * 0.05), $coinRect.Width - ($Bounds.Width * 0.1), $coinRect.Height - ($Bounds.Width * 0.1))
    $Graphics.DrawEllipse($ringPen, $ringRect)

    $panelRect = [System.Drawing.RectangleF]::new(
        $Bounds.X + ($Bounds.Width * 0.27),
        $Bounds.Y + ($Bounds.Height * 0.24),
        $Bounds.Width * 0.47,
        $Bounds.Height * 0.52)
    $panelBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new($panelRect.Left, $panelRect.Top),
        [System.Drawing.PointF]::new($panelRect.Right, $panelRect.Bottom),
        $tealDark,
        $tealLight)
    $panelPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $radius = $Bounds.Width * 0.06
    $diameter = $radius * 2
    $panelPath.AddArc($panelRect.Left, $panelRect.Top, $diameter, $diameter, 180, 90)
    $panelPath.AddArc($panelRect.Right - $diameter, $panelRect.Top, $diameter, $diameter, 270, 90)
    $panelPath.AddArc($panelRect.Right - $diameter, $panelRect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $panelPath.AddArc($panelRect.Left, $panelRect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $panelPath.CloseFigure()
    $Graphics.FillPath($panelBrush, $panelPath)

    $pageFold = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($panelRect.Right - ($panelRect.Width * 0.18), $panelRect.Top),
        [System.Drawing.PointF]::new($panelRect.Right, $panelRect.Top),
        [System.Drawing.PointF]::new($panelRect.Right, $panelRect.Top + ($panelRect.Height * 0.18))
    )
    $Graphics.FillPolygon([System.Drawing.SolidBrush]::new($brassLight), $pageFold)

    $paperBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(235, $paper.R, $paper.G, $paper.B))
    $lineBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220, $paper.R, $paper.G, $paper.B))

    $graphicsUnit = $Bounds.Width * 0.03
    Fill-RoundedRectangle -Graphics $Graphics -Brush $paperBrush -X ($panelRect.X + ($panelRect.Width * 0.17)) -Y ($panelRect.Y + ($panelRect.Height * 0.2)) -Width ($graphicsUnit * 1.4) -Height ($panelRect.Height * 0.56) -Radius ($graphicsUnit * 0.7)
    Fill-RoundedRectangle -Graphics $Graphics -Brush $lineBrush -X ($panelRect.X + ($panelRect.Width * 0.37)) -Y ($panelRect.Y + ($panelRect.Height * 0.25)) -Width ($panelRect.Width * 0.34) -Height ($graphicsUnit * 0.7) -Radius ($graphicsUnit * 0.35)
    Fill-RoundedRectangle -Graphics $Graphics -Brush $lineBrush -X ($panelRect.X + ($panelRect.Width * 0.37)) -Y ($panelRect.Y + ($panelRect.Height * 0.43)) -Width ($panelRect.Width * 0.28) -Height ($graphicsUnit * 0.7) -Radius ($graphicsUnit * 0.35)
    Fill-RoundedRectangle -Graphics $Graphics -Brush $lineBrush -X ($panelRect.X + ($panelRect.Width * 0.37)) -Y ($panelRect.Y + ($panelRect.Height * 0.61)) -Width ($panelRect.Width * 0.38) -Height ($graphicsUnit * 0.7) -Radius ($graphicsUnit * 0.35)
    Fill-RoundedRectangle -Graphics $Graphics -Brush $lineBrush -X ($panelRect.X + ($panelRect.Width * 0.37)) -Y ($panelRect.Y + ($panelRect.Height * 0.79)) -Width ($panelRect.Width * 0.22) -Height ($graphicsUnit * 0.7) -Radius ($graphicsUnit * 0.35)

    $coinBrush.Dispose()
    $ringPen.Dispose()
    $panelBrush.Dispose()
    $panelPath.Dispose()
    $paperBrush.Dispose()
    $lineBrush.Dispose()
}

function Fill-RoundedRectangle {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Brush]$Brush,
        [double]$X,
        [double]$Y,
        [double]$Width,
        [double]$Height,
        [double]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = [Math]::Min($Radius * 2, [Math]::Min($Width, $Height))
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $Graphics.FillPath($Brush, $path)
    $path.Dispose()
}

function Draw-Lockup {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Width,
        [int]$Height
    )

    $Graphics.Clear($canvas)
    Draw-Mark -Graphics $Graphics -Bounds ([System.Drawing.RectangleF]::new(12, 12, $Height - 24, $Height - 24))

    $titleFont = [System.Drawing.Font]::new("Georgia", [Math]::Max(22, $Height * 0.24), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $tagFont = [System.Drawing.Font]::new("Trebuchet MS", [Math]::Max(10, $Height * 0.08), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $titleBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 23, 60, 67))
    $tagBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(180, 23, 60, 67))

    $textLeft = $Height + 12
    $Graphics.DrawString("BrassLedger", $titleFont, $titleBrush, [System.Drawing.PointF]::new($textLeft, $Height * 0.26))
    $Graphics.DrawString("ACCOUNTING AND OPERATIONS", $tagFont, $tagBrush, [System.Drawing.PointF]::new($textLeft + 2, $Height * 0.64))

    $titleFont.Dispose()
    $tagFont.Dispose()
    $titleBrush.Dispose()
    $tagBrush.Dispose()
}

function Save-Png {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$Path
    )
    $directory = Split-Path -Parent $Path
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function Save-IcoFromPngBytes {
    param(
        [byte[]]$PngBytes,
        [string]$Path
    )

    $directory = Split-Path -Parent $Path
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = [System.IO.BinaryWriter]::new($stream)

    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]1)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$PngBytes.Length)
        $writer.Write([UInt32]22)
        $writer.Write($PngBytes)
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function New-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $memory = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
        return $memory.ToArray()
    }
    finally {
        $memory.Dispose()
    }
}

$mark512 = New-BrandBitmap -Width 512 -Height 512
Draw-Mark -Graphics $mark512.Graphics -Bounds ([System.Drawing.RectangleF]::new(0, 0, 512, 512))
Save-Png -Bitmap $mark512.Bitmap -Path (Join-Path $brandDir "brassledger-mark-512.png")
Save-Png -Bitmap $mark512.Bitmap -Path (Join-Path $Root "BrassLedger.Web\wwwroot\favicon.png")
$icoBytes = New-PngBytes -Bitmap $mark512.Bitmap
Save-IcoFromPngBytes -PngBytes $icoBytes -Path (Join-Path $webAssetDir "brassledger.ico")
$mark512.Graphics.Dispose()
$mark512.Bitmap.Dispose()

foreach ($size in 44, 50, 150, 310) {
    $canvasInfo = New-BrandBitmap -Width $size -Height $size
    Draw-Mark -Graphics $canvasInfo.Graphics -Bounds ([System.Drawing.RectangleF]::new(0, 0, $size, $size))

    switch ($size) {
        44 { $name = "Square44x44Logo.png" }
        50 { $name = "StoreLogo.png" }
        150 { $name = "Square150x150Logo.png" }
        310 { $name = "Square310x310Logo.png" }
    }

    Save-Png -Bitmap $canvasInfo.Bitmap -Path (Join-Path $msixAssetDir $name)
    $canvasInfo.Graphics.Dispose()
    $canvasInfo.Bitmap.Dispose()
}

$wide = New-BrandBitmap -Width 620 -Height 300
Draw-Lockup -Graphics $wide.Graphics -Width 620 -Height 300
Save-Png -Bitmap $wide.Bitmap -Path (Join-Path $msixAssetDir "Wide310x150Logo.png")
$wide.Graphics.Dispose()
$wide.Bitmap.Dispose()

$splash = New-BrandBitmap -Width 620 -Height 300
$splash.Graphics.Clear($canvas)
Draw-Mark -Graphics $splash.Graphics -Bounds ([System.Drawing.RectangleF]::new(155, 25, 210, 210))
$titleFont = [System.Drawing.Font]::new("Georgia", 34, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$titleBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 23, 60, 67))
$splash.Graphics.DrawString("BrassLedger", $titleFont, $titleBrush, [System.Drawing.PointF]::new(184, 242))
Save-Png -Bitmap $splash.Bitmap -Path (Join-Path $msixAssetDir "SplashScreen.png")
$titleFont.Dispose()
$titleBrush.Dispose()
$splash.Graphics.Dispose()
$splash.Bitmap.Dispose()
