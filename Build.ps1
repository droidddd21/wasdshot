$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$node = 'C:\Users\nero\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework compiler not found.' }
if (-not (Test-Path -LiteralPath $node)) { throw 'Bundled Node.js not found; install Node.js to rebuild the UI.' }
Push-Location (Join-Path $project 'ui')
try {
    & $node 'node_modules/typescript/bin/tsc' -b
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript check failed.' }
    $esbuild = 'node_modules/.pnpm/@esbuild+win32-x64@0.28.2/node_modules/@esbuild/win32-x64/esbuild.exe'
    & $esbuild 'src/main.tsx' '--bundle' '--outfile=dist/app.js' '--format=iife' '--platform=browser' '--target=es2022' '--jsx=automatic' '--alias:@=./src' '--loader:.png=dataurl' '--minify'
    if ($LASTEXITCODE -ne 0) { throw 'JavaScript build failed.' }
    & $node 'tests/pet-assets.test.mjs'
    if ($LASTEXITCODE -ne 0) { throw 'Pet asset export test failed.' }
    & $esbuild 'src/pet.ts' '--bundle' '--outfile=dist/pet.js' '--format=esm' '--platform=node' '--target=es2022'
    if ($LASTEXITCODE -ne 0) { throw 'Pet test build failed.' }
    & $node 'tests/pet.test.mjs'
    if ($LASTEXITCODE -ne 0) { throw 'Pet tests failed.' }
    & $node 'node_modules/@tailwindcss/cli/dist/index.mjs' -i 'src/index.css' -o 'dist/style.css' --minify
    if ($LASTEXITCODE -ne 0) { throw 'CSS build failed.' }
    Copy-Item 'index.html' -Destination 'dist/index.html' -Force
}
finally { Pop-Location }
$sdk = Join-Path $project 'sdk'
$release = Join-Path $project 'release-v3'
New-Item -ItemType Directory -Force -Path (Join-Path $release 'ui\dist'),(Join-Path $release 'assets') | Out-Null
& $compiler /nologo /target:winexe /optimize+ /platform:x64 "/out:$(Join-Path $release 'wasdshot.exe')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$(Join-Path $sdk 'Microsoft.Web.WebView2.Core.dll')" "/reference:$(Join-Path $sdk 'Microsoft.Web.WebView2.WinForms.dll')" (Join-Path $project 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
Copy-Item (Join-Path $sdk 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $sdk 'Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $sdk 'WebView2Loader.dll') -Destination $release -Force
Copy-Item (Join-Path $project 'ui\dist\index.html'),(Join-Path $project 'ui\dist\app.js'),(Join-Path $project 'ui\dist\style.css') -Destination (Join-Path $release 'ui\dist') -Force
Copy-Item (Join-Path $project 'assets\axolotl-tray-96.png'),(Join-Path $project 'assets\axolotl-tray-red-96.png'),(Join-Path $project 'assets\axolotl-tray-green-96.png') -Destination (Join-Path $release 'assets') -Force
Copy-Item (Join-Path $project 'README.md') -Destination $release -Force
$archive = Join-Path $project 'wasdshot-v3.zip'
Compress-Archive -LiteralPath $release -DestinationPath $archive -Force
$payload = Join-Path $project 'wasdshot-payload.zip'
Compress-Archive -Path (Join-Path $release '*') -DestinationPath $payload -Force
$setup = Join-Path $project 'wasdshot-setup-v3.exe'
& $compiler /nologo /target:winexe /optimize+ /platform:x64 "/out:$setup" /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$payload,wasdshot.payload.zip" (Join-Path $project 'Installer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Remove-Item -LiteralPath $payload -Force
Write-Output (Join-Path $release 'wasdshot.exe')
