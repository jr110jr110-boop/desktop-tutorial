Put the .NET 10 Hosting Bundle installer here only if the server has no internet access.

1. On a computer with internet access, open https://dotnet.microsoft.com/download/dotnet/10.0
2. Under "ASP.NET Core Runtime 10.0.x", Windows, click "Hosting Bundle".
   The file is named like dotnet-hosting-10.0.13-win.exe
3. Copy that file into this "installers" folder and run install.ps1 again.

With internet access nothing is needed here: install.ps1 downloads the latest .NET 10 Hosting Bundle
from Microsoft and checks its SHA-512 hash.
