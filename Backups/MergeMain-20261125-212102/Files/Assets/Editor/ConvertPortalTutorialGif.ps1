param([Parameter(Mandatory=$true)][string]$Source,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$Name='W')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$gif = [System.Drawing.Image]::FromFile($Source)
try {
    $dimension = New-Object System.Drawing.Imaging.FrameDimension($gif.FrameDimensionsList[0])
    $count = $gif.GetFrameCount($dimension)
    $columns = [Math]::Min(10, $count)
    $rows = [int][Math]::Ceiling($count / $columns)
    $atlas = New-Object System.Drawing.Bitmap(($gif.Width * $columns), ($gif.Height * $rows), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $graphics = [System.Drawing.Graphics]::FromImage($atlas)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $delays = $null
        if ($gif.PropertyIdList -contains 0x5100) { $delays = $gif.GetPropertyItem(0x5100).Value }
        $durations = @()
        for ($i = 0; $i -lt $count; $i++) {
            [void]$gif.SelectActiveFrame($dimension, $i)
            $graphics.DrawImageUnscaled($gif, (($i % $columns) * $gif.Width), ([int][Math]::Floor($i / $columns) * $gif.Height))
            $delay = .08
            if ($null -ne $delays -and $delays.Length -ge (($i + 1) * 4)) { $delay = [Math]::Max(.01, [BitConverter]::ToInt32($delays, $i * 4) / 100.0) }
            $durations += $delay
        }
        $atlas.Save((Join-Path $OutputDirectory ($Name + '_Frames.bytes')), [System.Drawing.Imaging.ImageFormat]::Png)
        $data = @{ width=$gif.Width; height=$gif.Height; columns=$columns; durations=$durations } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText((Join-Path $OutputDirectory ($Name + '.json')), $data, (New-Object System.Text.UTF8Encoding($false)))
    } finally { $graphics.Dispose(); $atlas.Dispose() }
} finally { $gif.Dispose() }
