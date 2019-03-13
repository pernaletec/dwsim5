using System;
namespace SkiaSharp.Views.Desktop
{
    public class SKPaintGLSurfaceEventArgs : EventArgs
	{
		public SKPaintGLSurfaceEventArgs(SKSurface surface, GRBackendRenderTargetDesc renderTarget)
		{
			Surface = surface;
			RenderTarget = renderTarget;
		}

		public SKSurface Surface { get; private set; }

		public GRBackendRenderTargetDesc RenderTarget { get; private set; }
	}
}
