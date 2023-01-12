﻿// <copyright file="RendererFactory.cs" company="KinsonDigital">
// Copyright (c) KinsonDigital. All rights reserved.
// </copyright>

namespace Velaptor.Factories;

using System.Diagnostics.CodeAnalysis;
using Carbonate;
using Graphics.Renderers;
using NativeInterop.OpenGL;
using OpenGL;
using OpenGL.Buffers;
using Services;

/// <inheritdoc/>
[ExcludeFromCodeCoverage(Justification = "Cannot unit test due direct interaction with IoC container.")]
public sealed class RendererFactory : IRendererFactory
{
    private static ITextureRenderer? textureRenderer;
    private static IFontRenderer? fontRenderer;
    private static IRectangleRenderer? rectangleRenderer;
    private static ILineRenderer? lineRenderer;

    /// <inheritdoc/>
    public ITextureRenderer CreateTextureRenderer()
    {
        if (textureRenderer is not null)
        {
            return textureRenderer;
        }

        var glInvoker = IoC.Container.GetInstance<IGLInvoker>();
        var reactable = IoC.Container.GetInstance<IPushReactable>();
        var openGLService = IoC.Container.GetInstance<IOpenGLService>();
        var buffer = IoC.Container.GetInstance<IGPUBuffer<TextureBatchItem>>();
        var shader = IoC.Container.GetInstance<IShaderFactory>().CreateTextureShader();
        var textureBatchManager = IoC.Container.GetInstance<IBatchingService<TextureBatchItem>>();

        textureRenderer = new TextureRenderer(
            glInvoker,
            reactable,
            openGLService,
            buffer,
            shader,
            textureBatchManager);

        return textureRenderer;
    }

    /// <inheritdoc/>
    public IFontRenderer CreateFontRenderer()
    {
        if (fontRenderer is not null)
        {
            return fontRenderer;
        }

        var glInvoker = IoC.Container.GetInstance<IGLInvoker>();
        var reactable = IoC.Container.GetInstance<IPushReactable>();
        var openGLService = IoC.Container.GetInstance<IOpenGLService>();
        var buffer = IoC.Container.GetInstance<IGPUBuffer<FontGlyphBatchItem>>();
        var shader = IoC.Container.GetInstance<IShaderFactory>().CreateFontShader();
        var fontBatchService = IoC.Container.GetInstance<IBatchingService<FontGlyphBatchItem>>();

        fontRenderer = new FontRenderer(
            glInvoker,
            reactable,
            openGLService,
            buffer,
            shader,
            fontBatchService);

        return fontRenderer;
    }

    /// <inheritdoc/>
    public IRectangleRenderer CreateRectangleRenderer()
    {
        if (rectangleRenderer is not null)
        {
            return rectangleRenderer;
        }

        var glInvoker = IoC.Container.GetInstance<IGLInvoker>();
        var reactable = IoC.Container.GetInstance<IPushReactable>();
        var openGLService = IoC.Container.GetInstance<IOpenGLService>();
        var buffer = IoC.Container.GetInstance<IGPUBuffer<RectBatchItem>>();
        var shader = IoC.Container.GetInstance<IShaderFactory>().CreateRectShader();
        var rectBatchService = IoC.Container.GetInstance<IBatchingService<RectBatchItem>>();

        rectangleRenderer = new RectangleRenderer(
            glInvoker,
            reactable,
            openGLService,
            buffer,
            shader,
            rectBatchService);

        return rectangleRenderer;
    }

    /// <inheritdoc/>
    public ILineRenderer CreateLineRenderer()
    {
        if (lineRenderer is not null)
        {
            return lineRenderer;
        }

        var glInvoker = IoC.Container.GetInstance<IGLInvoker>();
        var reactable = IoC.Container.GetInstance<IPushReactable>();
        var openGLService = IoC.Container.GetInstance<IOpenGLService>();
        var buffer = IoC.Container.GetInstance<IGPUBuffer<LineBatchItem>>();
        var shader = IoC.Container.GetInstance<IShaderFactory>().CreateLineShader();
        var lineBatchService = IoC.Container.GetInstance<IBatchingService<LineBatchItem>>();

        lineRenderer = new LineRenderer(
            glInvoker,
            reactable,
            openGLService,
            buffer,
            shader,
            lineBatchService);

        return lineRenderer;
    }
}
