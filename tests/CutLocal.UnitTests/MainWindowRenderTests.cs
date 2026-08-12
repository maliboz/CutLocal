using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CutLocal.App;
using CutLocal.App.Controls;
using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

public sealed class MainWindowRenderTests
{
    [Fact]
    public async Task MainWindow_LoadsResourcesAndRendersAtDesktopSizeOnSta()
    {
        TaskCompletionSource<Exception?> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RenderWindow(completion))
        {
            IsBackground = true,
            Name = "CutLocal WPF render test",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Null(exception);
    }

    private static void RenderWindow(TaskCompletionSource<Exception?> completion)
    {
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            CutLocal.App.App application = new();
            application.InitializeComponent();
            Phase3TestContext context = new();
            context.ViewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

            string inputRoot = Path.Combine(Path.GetTempPath(), "CutLocal.Tests", "ui-render");
            Directory.CreateDirectory(inputRoot);
            string input = Path.Combine(inputRoot, "örnek.png");
            File.WriteAllBytes(input, [0x89]);
            context.ViewModel.AcceptDroppedFilesAsync([input]).GetAwaiter().GetResult();

            string[] batchInputs = Enumerable.Range(0, 48)
                .Select(index => Path.Combine(inputRoot, $"batch-{index:D3}.png"))
                .ToArray();
            foreach (string batchInput in batchInputs)
            {
                File.WriteAllBytes(batchInput, [0x89]);
            }

            context.ViewModel.ShowBatchModeCommand.Execute(null);
            Assert.True(context.ViewModel.AcceptDroppedFilesAsync(batchInputs).GetAwaiter().GetResult());
            context.ViewModel.ShowSingleModeCommand.Execute(null);

            MainWindow window = new(context.ViewModel)
            {
                Width = 1380,
                Height = 860,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
            };
            window.Show();
            window.UpdateLayout();

            Assert.NotNull(window.Icon);
            Image brandLogo = Assert.IsType<Image>(window.FindName("BrandLogo"));
            Assert.NotNull(brandLogo.Source);

            int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            bitmap.Freeze();

            string artifactPath = SaveArtifact(bitmap, "phase-4-single-window.png");

            Assert.Equal(1380, bitmap.PixelWidth);
            Assert.Equal(860, bitmap.PixelHeight);
            Assert.True(new FileInfo(artifactPath).Length > 10_000);

            VerifyTransparentAfterPreviewDoesNotOverlapBeforePreview();

            context.ViewModel.ShowBatchModeCommand.Execute(null);
            window.UpdateLayout();

            ListView queue = Assert.IsType<ListView>(window.FindName("BatchQueueList"));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(queue));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(queue));
            RenderTargetBitmap batchBitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            batchBitmap.Render(window);
            batchBitmap.Freeze();
            string batchArtifact = SaveArtifact(batchBitmap, "phase-4-batch-window.png");
            Assert.True(new FileInfo(batchArtifact).Length > 10_000);

            ModelManagerViewModel modelManager = new(
                new ModelManagementUseCase(new RenderModelPackageManager()),
                new TestFileDialogService(),
                new LocalizationService(),
                NullLogger<ModelManagerViewModel>.Instance);
            modelManager.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            ModelManagerWindow modelWindow = new(modelManager)
            {
                Width = 1060,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
            };
            modelWindow.Show();
            modelWindow.UpdateLayout();
            Assert.NotNull(modelWindow.Icon);
            ListView catalog = Assert.IsType<ListView>(modelWindow.FindName("ModelCatalogList"));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(catalog));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(catalog));
            RenderTargetBitmap modelBitmap = new(1060, 720, 96, 96, PixelFormats.Pbgra32);
            modelBitmap.Render(modelWindow);
            modelBitmap.Freeze();
            string modelArtifact = SaveArtifact(modelBitmap, "phase-5-model-manager.png");
            Assert.True(new FileInfo(modelArtifact).Length > 10_000);
            CloseAndWait(modelWindow);
            CloseAndWait(window);
            application.Shutdown();
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void CloseAndWait(Window window)
    {
        bool closed = false;
        DispatcherFrame frame = new();
        window.Closed += (_, _) =>
        {
            closed = true;
            frame.Continue = false;
        };

        window.Close();
        if (!closed)
        {
            _ = window.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        Assert.True(closed);
    }

    private static void VerifyTransparentAfterPreviewDoesNotOverlapBeforePreview()
    {
        byte[] opaqueRed = [0, 0, 255, 255, 0, 0, 255, 255];
        byte[] transparent = [0, 0, 0, 0, 0, 0, 0, 0];
        BitmapSource before = BitmapSource.Create(
            2, 1, 96, 96, PixelFormats.Bgra32, null, opaqueRed, 8);
        BitmapSource after = BitmapSource.Create(
            2, 1, 96, 96, PixelFormats.Bgra32, null, transparent, 8);
        BeforeAfterViewer viewer = new()
        {
            BeforeSource = before,
            AfterSource = after,
        };
        Window comparisonWindow = new()
        {
            Width = 500,
            Height = 360,
            Content = viewer,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
        };

        comparisonWindow.Show();
        comparisonWindow.UpdateLayout();

        Image beforeImage = Assert.IsType<Image>(viewer.FindName("BeforeImage"));
        Image afterImage = Assert.IsType<Image>(viewer.FindName("AfterImage"));
        RectangleGeometry beforeClip = Assert.IsType<RectangleGeometry>(beforeImage.Clip);
        RectangleGeometry afterClip = Assert.IsType<RectangleGeometry>(afterImage.Clip);

        Assert.True(beforeClip.Rect.Width > 0);
        Assert.Equal(beforeClip.Rect.Right, afterClip.Rect.Left, precision: 5);
        Rect overlap = Rect.Intersect(beforeClip.Rect, afterClip.Rect);
        Assert.True(overlap.IsEmpty || overlap.Width == 0);
        CloseAndWait(comparisonWindow);
    }

    private static string SaveArtifact(RenderTargetBitmap bitmap, string fileName)
    {
        string artifactRoot = Path.Combine(Path.GetTempPath(), "CutLocal.Tests");
        Directory.CreateDirectory(artifactRoot);
        string artifactPath = Path.Combine(artifactRoot, fileName);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = new(artifactPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return artifactPath;
    }

    private sealed class RenderModelPackageManager : IModelPackageManager
    {
        private static readonly IReadOnlyList<ModelInstallationInfo> Items =
        [
            Create("u2netp", "U2NetP Fast", ModelInstallationState.Installed, 4_574_861, "Apache-2.0", ["cpu", "directml"]),
            Create("birefnet-general-lite", "BiRefNet General Lite", ModelInstallationState.NotInstalled, 224_005_088, "MIT", ["cpu"]),
        ];

        public ValueTask<IReadOnlyList<ModelInstallationInfo>> InspectAllAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(Items);

        public ValueTask<ModelPackageOperationResult> DownloadAsync(
            ModelDescriptor descriptor,
            IProgress<ModelTransferProgress>? progress,
            CancellationToken cancellationToken,
            bool licenseAcknowledged = false) => throw new NotSupportedException();

        public ValueTask<ModelPackageOperationResult> RepairAsync(
            ModelDescriptor descriptor,
            IProgress<ModelTransferProgress>? progress,
            CancellationToken cancellationToken,
            bool licenseAcknowledged = false) => throw new NotSupportedException();

        public ValueTask<ModelPackageOperationResult> DeleteAsync(
            ModelDescriptor descriptor,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ModelPackageOperationResult> ImportAsync(
            ModelImportRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        private static ModelInstallationInfo Create(
            string id,
            string name,
            ModelInstallationState state,
            long bytes,
            string license,
            IReadOnlyList<string> providers) => new()
            {
                Descriptor = new ModelDescriptor
                {
                    Id = id,
                    DisplayName = name,
                    Version = "1",
                    FileName = id + ".onnx",
                    Sha256 = new string('A', 64),
                    FileSizeBytes = bytes,
                    DownloadUrl = "https://example.test/model.onnx",
                    License = new ModelLicenseDescriptor
                    {
                        Spdx = license,
                        CommercialUseAllowed = true,
                        AttributionRequired = true,
                        Source = "https://example.test/license",
                    },
                    Input = new ModelInputDescriptor
                    {
                        Width = id == "u2netp" ? 320 : 1024,
                        Height = id == "u2netp" ? 320 : 1024,
                        Layout = "NCHW",
                        ColorOrder = "RGB",
                        Mean = [0.485, 0.456, 0.406],
                        Std = [0.229, 0.224, 0.225],
                        ResizeMode = "stretch",
                    },
                    Output = new ModelOutputDescriptor
                    {
                        Activation = "minmax",
                        Type = "alpha-mask",
                    },
                    RecommendedMemoryMb = 1024,
                    Tier = "test",
                    SupportedProviders = providers,
                },
                State = state,
                LocalBytes = state == ModelInstallationState.Installed ? bytes : 0,
            };
    }
}
