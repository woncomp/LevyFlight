# Quick-open XAML connection crash

## Symptoms

Opening LevyFlight's quick-open window threw a `System.Windows.Markup.XamlParseException` from `PresentationFramework`:

> Set connectionId threw an exception. Line number '10' and line position '14'.

The exception originated while constructing `LevyFlightQuickOpenControl`, before the window completed initialization.

## Diagnosis

The WPF message is only an outer wrapper around a failure in the generated
`IComponentConnector.Connect` method. The Experimental Instance's MEF catalog
error log contained the decisive inner loader failure:

> Could not load file or assembly `ICSharpCode.AvalonEdit, Version=6.3.0.90`

`ICSharpCode.AvalonEdit.dll` was present beside `LevyFlight.dll`, but the
extension directory was not registered as a Visual Studio assembly binding
path. `LevyFlightQuickOpenControl`'s generated connector has an AvalonEdit
field and cast. The CLR can therefore fail while loading or JIT-compiling the
connector, and WPF reports that failure as an exception while setting
`connectionId`.

Stale BAML was investigated because the old `0.1.0` BAML assigns connection
`1` to the root control at line 10, position 14, while the current `0.1.1.0`
BAML assigns connection `1` to the `TextBox` at line 26, position 14. After the
old deployment and component cache were removed, the exception still occurred.
The current deployed assembly hash matched the build output, its BAML and
connector were internally consistent, and only `0.1.1.0` was active. The line
mapping was useful stale-state evidence, but it was not the root cause.

## Change

Added `[ProvideBindingPath]` to `LevyFlightPackage`. The generated package
registration now adds `$PackageFolder$` to Visual Studio's assembly probing
paths, so the bundled AvalonEdit dependency can be resolved.

The root `Loaded`, `Unloaded`, `PreviewKeyDown`, `KeyDown`, and `KeyUp` handlers
remain attached explicitly in the control constructor after
`InitializeComponent()`.

## Verification

Built the solution with Visual Studio MSBuild using `DeployExtension=false`.

- Build completed with zero errors.
- No new warnings were introduced; the existing analyzer-warning baseline remained unchanged.
- The generated package registration contains the LevyFlight `BindingPaths` entry.
- The deployed `LevyFlight.dll` hash matches the rebuilt output and remains version `0.1.1.0`.
- The regenerated MEF cache contains no AvalonEdit or LevyFlight discovery error.

## Regression notes

- Keep root event hookups in code if this control's XAML is edited in the future.
- Do not validate XAML connection changes in an already-running Experimental Instance.
- Ensure only one LevyFlight version is deployed before diagnosing another `connectionId` failure.
- Check `ComponentModelCache/Microsoft.VisualStudio.Default.err` for dependency loader failures before attributing a `connectionId` wrapper to BAML.
- Keep the package binding-path registration while the VSIX ships private managed dependencies.
- Log the complete exception, including inner exceptions, whenever control construction fails.
