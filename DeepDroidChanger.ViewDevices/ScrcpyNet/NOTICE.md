# ScrcpyNet attribution

The `DeepDroidChanger.ViewDevices/ScrcpyNet` directory contains the
ScrcpyNet and ScrcpyNet.Wpf source from
https://github.com/Fusion86/ScrcpyNet at commit
80027a8203c0ddb87b1e2f56016a4630e1bd2e24.

The source and runtime files are retained under the upstream Apache License
2.0 and are intentionally limited to the functionality required by
DeepDroidChanger. The former separate ScrcpyNet and ScrcpyNet.Wpf projects
are merged into the DeepDroidChanger.ViewDevices assembly. Local lifecycle,
dynamic-port, touch-input, and WPF integration changes are maintained in this
repository; samples, benchmarks, and Avalonia projects are not included.

ScrcpyNet uses the scrcpy server implementation and FFmpeg native libraries.
See the dependency package metadata and the upstream projects for their
respective notices.
