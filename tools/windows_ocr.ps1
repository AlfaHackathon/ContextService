param([Parameter(Mandatory=$true)][string]$ImagePath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Globalization.Language,Windows.Globalization,ContentType=WindowsRuntime]
function Await-Result($Operation,[Type]$Type) {
  $method=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
  $task=$method.MakeGenericMethod($Type).Invoke($null,@($Operation))
  $task.Wait()
  return $task.Result
}
$file=Await-Result ([Windows.Storage.StorageFile]::GetFileFromPathAsync([System.IO.Path]::GetFullPath($ImagePath))) ([Windows.Storage.StorageFile])
$stream=Await-Result ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
$decoder=Await-Result ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
$bitmap=Await-Result ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
$engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('ru-RU'))
if(-not $engine){throw 'Russian Windows OCR language is unavailable'}
$result=Await-Result ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
$words=@(foreach($line in $result.Lines){foreach($word in $line.Words){@{text=$word.Text;left=$word.BoundingRect.X;top=$word.BoundingRect.Y;width=$word.BoundingRect.Width;height=$word.BoundingRect.Height;conf=100}}})
[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
ConvertTo-Json -InputObject $words -Depth 4 -Compress
$bitmap.Dispose();$stream.Dispose()
