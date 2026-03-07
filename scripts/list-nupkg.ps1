Add-Type -Assembly "System.IO.Compression.FileSystem"
$nupkg = "C:\Users\Evan\.nuget\packages\system.data.sqlite.core\1.0.119\system.data.sqlite.core.1.0.119.nupkg"
$zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
$zip.Entries | Select-Object -ExpandProperty FullName
$zip.Dispose()
