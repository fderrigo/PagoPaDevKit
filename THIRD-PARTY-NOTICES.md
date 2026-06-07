# Third-Party Notices

PagoPaDevKit bundles or depends on the third-party components listed below. Each
is the property of its respective owners and is distributed under its own
license. The PagoPaDevKit Apache-2.0 license does **not** apply to these
components.

## Vendored front-end assets

Located under `*/wwwroot/lib/`.

| Component | Version | License | Project |
|---|---|---|---|
| Bootstrap Italia | 2.18.0 | BSD-3-Clause | https://github.com/italia/bootstrap-italia |
| Bootstrap | 5.x | MIT | https://github.com/twbs/bootstrap |
| Bootstrap Icons | 1.x | MIT | https://github.com/twbs/icons |
| jQuery | 3.x | MIT | https://github.com/jquery/jquery |
| jQuery Validation | 1.x | MIT | https://github.com/jquery-validation/jquery-validation |
| jQuery Validation Unobtrusive | 4.x | MIT | https://github.com/aspnet/jquery-validation-unobtrusive |

### Fonts (bundled with Bootstrap Italia)

| Font | License | Project |
|---|---|---|
| Titillium Web | SIL Open Font License 1.1 | https://github.com/campitidesign/titillium |
| Roboto Mono | Apache License 2.0 | https://fonts.google.com/specimen/Roboto+Mono |

## NuGet / .NET dependencies

Managed via NuGet and restored at build time (not redistributed in source):

| Package | Version | License | Project |
|---|---|---|---|
| .NET 9 runtime & ASP.NET Core | 9.x | MIT | https://github.com/dotnet |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.8 | MIT | https://github.com/dotnet/efcore |
| Microsoft.Extensions.Http | 9.0.8 | MIT | https://github.com/dotnet/runtime |
| SoapCore | 1.2.1.13 | MIT | https://github.com/DigDes/SoapCore |
| Swashbuckle.AspNetCore | 10.2.1 | MIT | https://github.com/domaindrivendev/Swashbuckle.AspNetCore |
| System.Security.Cryptography.Xml | 10.0.8 | MIT | https://github.com/dotnet/runtime |
| QRCoder | 1.8.0 | MIT | https://github.com/codebude/QRCoder |

Test-only dependencies (not shipped at runtime): xUnit (Apache-2.0),
Microsoft.NET.Test.Sdk (MIT), Microsoft.AspNetCore.Mvc.Testing (MIT),
coverlet.collector (MIT).

The license texts for the components above are available from their respective
project repositories linked in this document.
