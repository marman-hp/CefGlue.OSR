using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.Common.InternalHandlers;

namespace Xilium.CefGlue.Common.Composition
{
    public sealed class CefGlueBuilder
    {
        public IServiceCollection Services { get; }

        internal CefGlueBuilder(IServiceCollection services)
        {
            Services = services;
            RegisterDefaults();
        }

        private void RegisterDefaults()
        {
            Services.AddCoordinator<IMouseCoordinator, DefaultMouseCoordinator>();
            Services.AddCoordinator<IKeyboardCoordinator, DefaultKeyboardCoordinator>();
            Services.AddCoordinator<IDragDropCoordinator, DefaultDragDropCoordinator>();
            Services.AddCoordinator<IResizeCoordinator, DefaultResizeCoordinator>();
            Services.AddCoordinator<IPaintDispatcher, DefaultPaintDispatcher>();
            Services.AddCoordinator<IContextMenuPresenter, DefaultContextMenuPresenter>();
            Services.AddCoordinator<IOffscreenRenderCallback, DefaultOffscreenRenderCallback>();

            Services.AddCoordinator<CefLifeSpanHandler, CommonCefLifeSpanHandler>();
        }

        public CefGlueHost Build() => new CefGlueHost(Services, Services.BuildServiceProvider());
    }
}
