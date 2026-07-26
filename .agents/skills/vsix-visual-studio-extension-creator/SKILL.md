---
name: vsix-visual-studio-extension-creator
description: Create, repair, or extend Visual Studio 2022 VSIX extensions. Use this skill whenever a user asks for a Visual Studio extension, VSIX, VSPackage, .vsct command table, Visual Studio menu command, toolbar button, extension icon, or Experimental Instance debugging. Use it for any task that creates or modifies a VSIX project so the result has the native VSIX project properties and Current Instance debugging experience.
---

# Visual Studio VSIX Extension Creator

Create Visual Studio extensions as classic C# VSSDK projects. This project system supplies the VSIX project property pages, package/deployment targets, manifest tooling, and the Visual Studio **Current Instance** debug target.

## Project layout

Use this structure unless the user requests another layout:

```text
<repository root>\
  <SolutionName>.sln
  src\
    <ExtensionName>\
      <ExtensionName>.csproj
      <ExtensionName>Package.cs
      <ExtensionName>Command.cs
      <ExtensionName>.vsct
      source.extension.vsixmanifest
      Resources\
```

Place the `.sln` at the repository root. Create the extension project in `src\<ExtensionName>`.

Use `vswhere` to identify the installed Visual Studio instance and version:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
  -latest -products * -format json
```

## Create the solution

Create a solution, then add the classic project file:

```powershell
dotnet new sln --name <SolutionName>
dotnet sln <SolutionName>.sln add src\<ExtensionName>\<ExtensionName>.csproj
```

The solution uses the normal C# project entry GUID. The extension project itself declares the VSSDK project type.

## Create the VSSDK project

Use a classic MSBuild C# project with:

- VSIX project type: `{82B43B9B-A64C-4715-B499-D71E9CA2BD60}`
- C# project type: `{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`
- .NET Framework 4.7.2
- a new stable project GUID
- Visual Studio 2022 VSSDK imports

Use this as the project-file foundation, replacing placeholders:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="17.0" DefaultTargets="Build"
         xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <MinimumVisualStudioVersion>17.0</MinimumVisualStudioVersion>
    <VisualStudioVersion Condition="'$(VisualStudioVersion)' == ''">17.0</VisualStudioVersion>
    <VSToolsPath Condition="'$(VSToolsPath)' == ''">$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)</VSToolsPath>
  </PropertyGroup>

  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"
          Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />

  <PropertyGroup>
    <Configuration Condition="'$(Configuration)' == ''">Debug</Configuration>
    <Platform Condition="'$(Platform)' == ''">AnyCPU</Platform>
    <SchemaVersion>2.0</SchemaVersion>
    <ProjectTypeGuids>{82B43B9B-A64C-4715-B499-D71E9CA2BD60};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <ProjectGuid>{PROJECT-GUID}</ProjectGuid>
    <OutputType>Library</OutputType>
    <RootNamespace>ExtensionName</RootNamespace>
    <AssemblyName>ExtensionName</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <GeneratePkgDefFile>true</GeneratePkgDefFile>
    <UseCodebase>true</UseCodebase>
    <IncludeAssemblyInVSIXContainer>true</IncludeAssemblyInVSIXContainer>
    <IncludeDebugSymbolsInVSIXContainer>false</IncludeDebugSymbolsInVSIXContainer>
    <IncludeDebugSymbolsInLocalVSIXDeployment>false</IncludeDebugSymbolsInLocalVSIXDeployment>
    <CopyBuildOutputToOutputDirectory>true</CopyBuildOutputToOutputDirectory>
    <CopyOutputSymbolsToOutputDirectory>true</CopyOutputSymbolsToOutputDirectory>
    <StartAction>Program</StartAction>
    <StartProgram Condition="'$(DevEnvDir)' != ''">$(DevEnvDir)devenv.exe</StartProgram>
    <StartArguments>/rootsuffix Exp</StartArguments>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'">
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>bin\Debug\</OutputPath>
    <DefineConstants>TRACE;DEBUG</DefineConstants>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Release|AnyCPU'">
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>bin\Release\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="ExtensionNamePackage.cs" />
    <Compile Include="ExtensionNameCommand.cs" />
  </ItemGroup>

  <ItemGroup>
    <None Include="source.extension.vsixmanifest">
      <SubType>Designer</SubType>
      <Generator>VsixManifestGenerator</Generator>
    </None>
  </ItemGroup>

  <ItemGroup>
    <Reference Include="System" />
    <Reference Include="System.Core" />
    <Reference Include="System.Design" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.VisualStudio.SDK" Version="<VS-compatible-version>" ExcludeAssets="runtime" />
    <PackageReference Include="Microsoft.VSSDK.BuildTools" Version="<VS-compatible-version>">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
  <Import Project="$(VSToolsPath)\VSSDK\Microsoft.VsSDK.targets"
          Condition="Exists('$(VSToolsPath)\VSSDK\Microsoft.VsSDK.targets')" />
</Project>
```

Use package versions compatible with the installed Visual Studio release. The installed VSSDK target path for Visual Studio 2022 is usually:

```text
<Visual Studio install>\MSBuild\Microsoft\VisualStudio\v17.0\VSSDK\Microsoft.VsSDK.targets
```

## Add the VSIX manifest

Create `source.extension.vsixmanifest` with the VSIX and design namespaces. Keep `PackageManifest` and all structural elements in the default VSIX namespace.

```xml
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0"
  xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"
  xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
  <Metadata>
    <Identity Id="ExtensionName.PROJECT-GUID" Version="1.0.0" Language="en-US" Publisher="Publisher" />
    <DisplayName>ExtensionName</DisplayName>
    <Description xml:space="preserve">Visual Studio extension.</Description>
  </Metadata>
  <Installation>
    <InstallationTarget Version="[17.0,18.0)" Id="Microsoft.VisualStudio.Community">
      <ProductArchitecture>amd64</ProductArchitecture>
    </InstallationTarget>
  </Installation>
  <Prerequisites>
    <Prerequisite Id="Microsoft.VisualStudio.Component.CoreEditor"
                  Version="[17.0,18.0)"
                  DisplayName="Visual Studio core editor" />
  </Prerequisites>
  <Assets>
    <Asset Type="Microsoft.VisualStudio.VsPackage"
           d:Source="Project"
           d:ProjectName="%CurrentProject%"
           Path="|%CurrentProject%;PkgdefProjectOutputGroup|" />
  </Assets>
</PackageManifest>
```

Add Professional and Enterprise installation targets when the extension supports those editions.

## Create the package and command

Create an `AsyncPackage`:

```csharp
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
public sealed class ExtensionNamePackage : AsyncPackage
{
    public const string PackageGuidString = "PROJECT-GUID";

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await ExtensionNameCommand.InitializeAsync(this);
    }
}
```

Register a command through `OleMenuCommandService` after switching to the main thread. Use the command-set GUID and command ID declared in the VSCT.

## Add menu commands and toolbar buttons when requested

Add the VSCT item to the project:

```xml
<ItemGroup>
  <VSCTCompile Include="ExtensionName.vsct">
    <ResourceName>Menus.ctmenu</ResourceName>
    <Generator>VsctGenerator</Generator>
  </VSCTCompile>
</ItemGroup>
```

### Add commands to existing top-level menus

Do not declare replacement `Menu` elements for Visual Studio's built-in top-level menus. Add a custom group beneath the target menu, then parent the button to that group.

Use these parents for the normal top-level menus:

| Visual Studio menu | Group parent |
|---|---|
| File | `IDM_VS_MENU_FILE` |
| Edit | `IDM_VS_MENU_EDIT` |
| View | `IDM_VS_MENU_VIEW` |
| Project | `IDM_VS_MENU_PROJECT` |
| Build | `IDM_VS_MENU_BUILD` |
| Debug | `IDM_VS_MENU_DEBUG` |
| Tools | `IDM_VS_MENU_TOOLS` |
| Window | `IDM_VS_MENU_WINDOW` |
| Help | `IDM_VS_MENU_HELP` |

Example:

```xml
<Group guid="guidExtensionCommandSet" id="FileGroup" priority="0x7F00">
  <Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_FILE" />
</Group>

<Button guid="guidExtensionCommandSet" id="FileCommand"
        priority="0x0100" type="Button">
  <Parent guid="guidExtensionCommandSet" id="FileGroup" />
  <Strings><ButtonText>Extension command</ButtonText></Strings>
</Button>
```

The **Extensions** menu is the exception. In Visual Studio 2022, parent the button directly to `IDG_VS_MM_TOOLSADDINS`. Do not add an intermediate custom group and do not use `IDM_VS_MENU_ADDINS`.

```xml
<Button guid="guidExtensionCommandSet" id="ExtensionsCommand"
        priority="0x7F00" type="Button">
  <Parent guid="guidSHLMainMenu" id="IDG_VS_MM_TOOLSADDINS" />
  <Strings><ButtonText>Extension command</ButtonText></Strings>
</Button>
```

To add one command to every supported top-level menu, declare one group for each normal menu and one button per menu. Each button needs a unique numeric ID and must be registered with `OleMenuCommandService`.

```xml
<Groups>
  <Group guid="guidExtensionCommandSet" id="FileGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_FILE" /></Group>
  <Group guid="guidExtensionCommandSet" id="EditGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_EDIT" /></Group>
  <Group guid="guidExtensionCommandSet" id="ViewGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_VIEW" /></Group>
  <Group guid="guidExtensionCommandSet" id="ProjectGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_PROJECT" /></Group>
  <Group guid="guidExtensionCommandSet" id="BuildGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_BUILD" /></Group>
  <Group guid="guidExtensionCommandSet" id="DebugGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_DEBUG" /></Group>
  <Group guid="guidExtensionCommandSet" id="ToolsGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_TOOLS" /></Group>
  <Group guid="guidExtensionCommandSet" id="WindowGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_WINDOW" /></Group>
  <Group guid="guidExtensionCommandSet" id="HelpGroup" priority="0x7F00"><Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_HELP" /></Group>
</Groups>
```

Avoid `IDG_VS_FILE_*`, `IDG_VS_EDIT_*`, and similar built-in submenu groups unless the command intentionally belongs in that exact built-in section. Avoid `IDG_VS_MM_*` menu-bar layout groups except for the verified Extensions-menu placement above.

When modifying an existing VSCT:

- Reuse its package GUID and command-set GUID.
- Preserve existing command and group IDs; allocate new unique IDs for additions.
- Register every new command ID in C#.
- Increment the version in `[ProvideMenuResource("Menus.ctmenu", version)]` after changing menu structure so Visual Studio re-merges the command table.
- Remove stale or duplicate Experimental Instance deployments before verification. Use F5 from the VSSDK project and ensure no old hidden `devenv.exe /rootsuffix Exp` process remains.

For a custom toolbar, declare a `Menu` with `type="Toolbar"`, add a group under it, and place the command in that group through `CommandPlacement`. A single command can appear in a built-in menu and on the toolbar.

For a custom icon, use Visual Studio's image service through an `.imagemanifest`; do not use the legacy VSCT `<Bitmaps>` mechanism. Give the image library its own stable GUID and numeric ID, and use the same values in the image manifest and VSCT:

```xml
<Commands package="guidExtensionPackage">
  <Groups>
    <Group guid="guidExtensionCommandSet" id="EditGroup" priority="0x0600">
      <Parent guid="guidSHLMainMenu" id="IDM_VS_MENU_EDIT" />
    </Group>
    <Group guid="guidExtensionCommandSet" id="ToolbarGroup" priority="0x0100">
      <Parent guid="guidExtensionCommandSet" id="ExtensionToolbar" />
    </Group>
  </Groups>
  <Menus>
    <Menu guid="guidExtensionCommandSet" id="ExtensionToolbar" type="Toolbar">
      <Strings><ButtonText>Extension Toolbar</ButtonText></Strings>
    </Menu>
  </Menus>
  <Buttons>
    <Button guid="guidExtensionCommandSet" id="ExtensionCommand" priority="0x0100" type="Button">
      <Parent guid="guidExtensionCommandSet" id="EditGroup" />
      <Icon guid="guidExtensionImages" id="ExtensionIcon" />
      <CommandFlag>IconIsMoniker</CommandFlag>
      <Strings><ButtonText>Extension Command</ButtonText></Strings>
    </Button>
  </Buttons>
</Commands>

<CommandPlacements>
  <CommandPlacement guid="guidExtensionCommandSet" id="ExtensionCommand" priority="0x0100">
    <Parent guid="guidExtensionCommandSet" id="ToolbarGroup" />
  </CommandPlacement>
</CommandPlacements>

<Symbols>
  <GuidSymbol name="guidExtensionPackage" value="{PACKAGE-GUID}" />
  <GuidSymbol name="guidExtensionCommandSet" value="{COMMAND-SET-GUID}">
    <!-- command, group, and toolbar IDs -->
  </GuidSymbol>
  <GuidSymbol name="guidExtensionImages" value="{IMAGE-LIBRARY-GUID}">
    <IDSymbol name="ExtensionIcon" value="1" />
  </GuidSymbol>
</Symbols>
```

`IconIsMoniker` is essential. Without it, VSCT interprets the GUID/ID pair as a legacy bitmap resource and the command usually renders with a blank icon.

Do not add `IconAndText` unless the user explicitly wants text on the toolbar. It can force a text/dropdown-style toolbar presentation instead of the normal icon-only button. Keep `ButtonText` for menus, tooltips, and accessibility.

## Integrate custom icons with ImageManifest

Convert an SVG or other vector source into WPF XAML. For Visual Studio command images, prefer a `Viewbox` visual root containing the drawing; a standalone `DrawingImage` can load successfully through WPF yet still fail to paint through the Visual Studio image service.

Also generate a faithful 16x16 PNG from the same source. Visual Studio 2022 command bars may not select a vector-only XAML source at the requested size, so use the sized raster as the reliable command-bar source and retain XAML as the scalable fallback.

Create `CustomMonikers.imagemanifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ImageManifest xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
               xmlns:xsd="http://www.w3.org/2001/XMLSchema"
               xmlns="http://schemas.microsoft.com/VisualStudio/ImageManifestSchema/2014">
  <Symbols>
    <Guid Name="ExtensionImagesGuid" Value="{IMAGE-LIBRARY-GUID}" />
    <ID Name="ExtensionIcon" Value="1" />
    <String Name="Resources" Value="/ExtensionAssembly;Component/Resources" />
  </Symbols>
  <Images>
    <Image Guid="$(ExtensionImagesGuid)" ID="$(ExtensionIcon)">
      <Source Uri="$(Resources)/ExtensionIcon.16.16.png">
        <Size Value="16" />
      </Source>
      <Source Uri="$(Resources)/ExtensionIcon.xaml" />
    </Image>
  </Images>
  <ImageLists />
</ImageManifest>
```

The assembly name and casing in the pack URI must match the built assembly. Compile the XAML as WPF `Page`, the PNG as `Resource`, and include the image manifest in the VSIX:

```xml
<ItemGroup>
  <Page Include="Resources\ExtensionIcon.xaml">
    <Generator>MSBuild:Compile</Generator>
    <SubType>Designer</SubType>
  </Page>
  <Resource Include="Resources\ExtensionIcon.16.16.png" />
  <Content Include="CustomMonikers.imagemanifest">
    <IncludeInVSIX>true</IncludeInVSIX>
  </Content>
</ItemGroup>

<ItemGroup>
  <Reference Include="PresentationCore" />
  <Reference Include="PresentationFramework" />
  <Reference Include="WindowsBase" />
</ItemGroup>
```

Register the manifest in generated pkgdef output. Use Unicode because pkgdef files are UTF-16:

```xml
<Target Name="RegisterCustomMonikers" AfterTargets="GeneratePkgDef">
  <WriteLinesToFile File="$(IntermediateOutputPath)$(TargetName).pkgdef"
                    Lines="[$RootKey$\ImageLibrary\Monikers\{IMAGE-LIBRARY-GUID}]"
                    Encoding="Unicode"
                    Overwrite="false" />
  <WriteLinesToFile File="$(IntermediateOutputPath)$(TargetName).pkgdef"
                    Lines="&quot;Manifest&quot;=&quot;$PackageFolder$\CustomMonikers.imagemanifest&quot;"
                    Encoding="Unicode"
                    Overwrite="false" />
</Target>
```

Declare the manifest as an image-library VSIX asset as well as a packaged content file:

```xml
<Asset Type="Microsoft.VisualStudio.ImageLibrary"
       Path="CustomMonikers.imagemanifest" />
```

The image service can request the assembly resource before the package initializes. Add an assembly codebase entry with the actual assembly name and assembly version:

```csharp
[assembly: ProvideCodeBase(
    AssemblyName = "ExtensionAssembly",
    Version = "1.0.0.0",
    CodeBase = "$PackageFolder$\\ExtensionAssembly.dll")]
```

Confirm the assembly version rather than assuming it matches the VSIX manifest version; they are independent.

### ImageManifest pitfalls

- Keep the image-library GUID and numeric ID identical in `.imagemanifest`, `.vsct`, and any C# `ImageMoniker`.
- Do not rely on successful build, pack-URI loading, or presence in the VSIX as proof that Visual Studio will paint the icon. Verify it in an Experimental Instance.
- Inspect the built VSIX for the `.imagemanifest`, DLL, and `.pkgdef`, then inspect the generated `.pkgdef` for both the `ImageLibrary\Monikers` registration and assembly codebase.
- Close the Experimental Instance between icon-registration changes. Visual Studio caches image-library and extension state aggressively; increment the VSIX version when redeploying during iterative verification.
- **Blank custom icons despite correct VSCT and manifest registration:** the Experimental Instance may have an `ImageLibrary\ImageLibrary.cache` created before the extension's monikers were deployed. With every Experimental Instance closed, delete `%LOCALAPPDATA%\Microsoft\VisualStudio\<instance-id>Exp\ImageLibrary\ImageLibrary.cache`, run `devenv.exe /RootSuffix Exp /UpdateConfiguration`, then launch the Experimental Instance again (preferably through F5). This forces the image service to index the extension's `.imagemanifest`; check the rendered menu icons rather than assuming the command table has refreshed the image cache.
- Keep the original SVG only as source material unless the extension directly consumes it. Visual Studio's image service consumes the compiled XAML/PNG resources, not the SVG.

## Build and debug

Build classic VSSDK projects with the MSBuild installed alongside Visual Studio:

```powershell
$msbuild = "<Visual Studio install>\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild "<SolutionName>.sln" "/t:Restore;Build" /p:Configuration=Debug "/p:Platform=Any CPU"
```

The build produces `<ExtensionName>.dll`, `<ExtensionName>.pkgdef`, and `<ExtensionName>.vsix` under `bin\Debug`.

Open the solution in Visual Studio. The project Properties window provides the VSIX property pages, and F5 uses the **Current Instance [Visual Studio Community 2022]** target to launch:

```text
devenv.exe /rootsuffix Exp
```

Confirm that the VSIX archive includes `extension.vsixmanifest`, the extension DLL, and the generated `.pkgdef`. Confirm deployment by checking the newest VSIX installer log in `%TEMP%\dd_VSIXInstaller*.log` or the extension directory beneath:

```text
%LOCALAPPDATA%\Microsoft\VisualStudio\<instance-id>Exp\Extensions
```
