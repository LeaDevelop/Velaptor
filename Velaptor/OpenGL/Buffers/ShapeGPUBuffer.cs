// <copyright file="ShapeGPUBuffer.cs" company="KinsonDigital">
// Copyright (c) KinsonDigital. All rights reserved.
// </copyright>

namespace Velaptor.OpenGL.Buffers;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Batching;
using Carbonate.UniDirectional;
using Exceptions;
using Factories;
using GPUData;
using Graphics;
using NativeInterop.OpenGL;
using ReactableData;

/// <summary>
/// Updates data in the rectangle GPU buffer.
/// </summary>
[GPUBufferName("Rectangle")]
[SuppressMessage("csharpsquid", "S101", Justification = "GPU is an acceptable acronym.")]
internal sealed class ShapeGPUBuffer : GPUBufferBase<ShapeBatchItem>
{
    private const string BufferNotInitMsg = "The rectangle buffer has not been initialized.";
    private readonly IDisposable unsubscriber;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShapeGPUBuffer"/> class.
    /// </summary>
    /// <param name="gl">Invokes OpenGL functions.</param>
    /// <param name="openGLService">Provides OpenGL related helper methods.</param>
    /// <param name="reactableFactory">Creates reactables for sending and receiving notifications with or without data.</param>
    /// <exception cref="ArgumentNullException">
    ///     Invoked when any of the parameters are null.
    /// </exception>
    public ShapeGPUBuffer(
        IGLInvoker gl,
        IOpenGLService openGLService,
        IReactableFactory reactableFactory)
            : base(gl, openGLService, reactableFactory)
    {
        var batchSizeReactable = reactableFactory.CreateBatchSizeReactable();

        var batchSizeName = this.GetExecutionMemberName(nameof(PushNotifications.BatchSizeChangedId));
        this.unsubscriber = batchSizeReactable.Subscribe(new ReceiveReactor<BatchSizeData>(
            eventId: PushNotifications.BatchSizeChangedId,
            name: batchSizeName,
            onReceiveData: data =>
            {
                if (data.TypeOfBatch != BatchType.Rect)
                {
                    return;
                }

                BatchSize = data.BatchSize;

                if (IsInitialized)
                {
                    ResizeBatch();
                }
            }));
    }

    /// <inheritdoc/>
    protected internal override void UploadVertexData(ShapeBatchItem rectShape, uint batchIndex)
    {
        OpenGLService.BeginGroup($"Update Rectangle - BatchItem({batchIndex})");

        /*
         * Always have the smallest value between the width and height (divided by 2)
         * as the maximum limit of what the border thickness can be.
         * If the value was allowed to be larger than the smallest value between
         * the width and height, it would produce unintended rendering artifacts.
         */

        rectShape = ProcessBorderThicknessLimit(rectShape);
        rectShape = ProcessCornerRadiusLimits(rectShape);

        var data = RectGPUData.Empty();
        var halfWidth = rectShape.Width / 2f;
        var halfHeight = rectShape.Height / 2f;

        var left = rectShape.Position.X - halfWidth;
        var bottom = rectShape.Position.Y + halfHeight;
        var right = rectShape.Position.X + halfWidth;
        var top = rectShape.Position.Y - halfHeight;

        var topLeft = new Vector2(left, top);
        var bottomLeft = new Vector2(left, bottom);
        var bottomRight = new Vector2(right, bottom);
        var topRight = new Vector2(right, top);

        data = data.SetVertexPos(topLeft.ToNDC(ViewPortSize.Width, ViewPortSize.Height), VertexNumber.One);
        data = data.SetVertexPos(bottomLeft.ToNDC(ViewPortSize.Width, ViewPortSize.Height), VertexNumber.Two);
        data = data.SetVertexPos(topRight.ToNDC(ViewPortSize.Width, ViewPortSize.Height), VertexNumber.Three);
        data = data.SetVertexPos(bottomRight.ToNDC(ViewPortSize.Width, ViewPortSize.Height), VertexNumber.Four);

        data = data.SetRectangle(new Vector4(rectShape.Position.X, rectShape.Position.Y, rectShape.Width, rectShape.Height));

        data = ApplyColor(data, rectShape);

        data = data.SetAsSolid(rectShape.IsSolid);

        data = data.SetBorderThickness(rectShape.BorderThickness);
        data = data.SetTopLeftCornerRadius(rectShape.CornerRadius.TopLeft);
        data = data.SetBottomLeftCornerRadius(rectShape.CornerRadius.BottomLeft);
        data = data.SetBottomRightCornerRadius(rectShape.CornerRadius.BottomRight);
        data = data.SetTopRightCornerRadius(rectShape.CornerRadius.TopRight);

        var totalBytes = RectGPUData.GetTotalBytes();
        var rawData = data.ToArray();
        var offset = totalBytes * batchIndex;

        OpenGLService.BindVBO(VBO);

        GL.BufferSubData(GLBufferTarget.ArrayBuffer, (nint)offset, totalBytes, rawData);

        OpenGLService.UnbindVBO();

        OpenGLService.EndGroup();
    }

    /// <inheritdoc/>
    protected internal override void PrepareForUpload()
    {
        if (IsInitialized is false)
        {
            throw new BufferNotInitializedException(BufferNotInitMsg);
        }

        OpenGLService.BindVAO(VAO);
    }

    /// <inheritdoc/>
    protected internal override float[] GenerateData()
    {
        var result = new List<float>();

        for (var i = 0u; i < BatchSize; i++)
        {
            var vertexData = GenerateVertexData();
            result.AddRange(new RectGPUData(vertexData[0], vertexData[1], vertexData[2], vertexData[3]).ToArray());
        }

        return result.ToArray();
    }

    /// <inheritdoc/>
    protected internal override void SetupVAO()
    {
        var stride = RectVertexData.GetStride();

        // Vertex Position
        const uint vertexPosSize = 2u * sizeof(float);
        GL.VertexAttribPointer(0, 2, GLVertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);

        // Rectangle
        const uint rectOffset = vertexPosSize;
        const uint rectSize = 4u * sizeof(float);
        GL.VertexAttribPointer(1, 4, GLVertexAttribPointerType.Float, false, stride, rectOffset);
        GL.EnableVertexAttribArray(1);

        // Color
        const uint colorOffset = rectOffset + rectSize;
        const uint colorSize = 4u * sizeof(float);
        GL.VertexAttribPointer(2, 4, GLVertexAttribPointerType.Float, false, stride, colorOffset);
        GL.EnableVertexAttribArray(2);

        // IsSolid
        const uint isSolidOffset = colorOffset + colorSize;
        const uint isSolidSize = 1u * sizeof(float);
        GL.VertexAttribPointer(3, 1, GLVertexAttribPointerType.Float, false, stride, isSolidOffset);
        GL.EnableVertexAttribArray(3);

        // Border Thickness
        const uint borderThicknessOffset = isSolidOffset + isSolidSize;
        const uint borderThicknessSize = 1u * sizeof(float);
        GL.VertexAttribPointer(4, 1, GLVertexAttribPointerType.Float, false, stride, borderThicknessOffset);
        GL.EnableVertexAttribArray(4);

        // Top Left Corner Radius
        const uint topLeftRadiusOffset = borderThicknessOffset + borderThicknessSize;
        const uint topLeftRadiusSize = 1u * sizeof(float);
        GL.VertexAttribPointer(5, 1, GLVertexAttribPointerType.Float, false, stride, topLeftRadiusOffset);
        GL.EnableVertexAttribArray(5);

        // Bottom Left Corner Radius
        const uint bottomLeftRadiusOffset = topLeftRadiusOffset + topLeftRadiusSize;
        const uint bottomLeftRadiusSize = 1u * sizeof(float);
        GL.VertexAttribPointer(6, 1, GLVertexAttribPointerType.Float, false, stride, bottomLeftRadiusOffset);
        GL.EnableVertexAttribArray(6);

        // Bottom Right Corner Radius
        const uint bottomRightRadiusOffset = bottomLeftRadiusOffset + bottomLeftRadiusSize;
        const uint bottomRightRadiusSize = 1u * sizeof(float);
        GL.VertexAttribPointer(7, 1, GLVertexAttribPointerType.Float, false, stride, bottomRightRadiusOffset);
        GL.EnableVertexAttribArray(7);

        // Top Right Corner Radius
        const uint topRightRadiusOffset = bottomRightRadiusOffset + bottomRightRadiusSize;
        GL.VertexAttribPointer(8, 1, GLVertexAttribPointerType.Float, false, stride, topRightRadiusOffset);
        GL.EnableVertexAttribArray(8);
    }

    /// <inheritdoc/>
    protected internal override uint[] GenerateIndices()
    {
        var result = new List<uint>();

        for (var i = 0u; i < BatchSize; i++)
        {
            var maxIndex = result.Count <= 0 ? 0 : result.Max() + 1;

            result.AddRange(new[]
            {
                maxIndex,
                maxIndex + 1u,
                maxIndex + 2u,
                maxIndex + 2u,
                maxIndex + 1u,
                maxIndex + 3u,
            });
        }

        return result.ToArray();
    }

    /// <inheritdoc/>
    protected override void ShutDown()
    {
        if (IsDisposed)
        {
            return;
        }

        this.unsubscriber.Dispose();

        base.ShutDown();
    }

    /// <summary>
    /// Generates default <see cref="RectVertexData"/> for all four vertices that make
    /// up a rectangle rendering area.
    /// </summary>
    /// <returns>The four vertex data items.</returns>
    private static RectVertexData[] GenerateVertexData()
    {
        var vertex1 = new RectVertexData(
            new Vector2(-1.0f, 1.0f),
            Vector4.Zero,
            Color.Empty,
            false,
            1f,
            0f,
            0f,
            0f,
            0f);

        var vertex2 = new RectVertexData(
            new Vector2(-1.0f, -1.0f),
            Vector4.Zero,
            Color.Empty,
            false,
            1f,
            0f,
            0f,
            0f,
            0f);

        var vertex3 = new RectVertexData(
            new Vector2(1.0f, 1.0f),
            Vector4.Zero,
            Color.Empty,
            false,
            1f,
            0f,
            0f,
            0f,
            0f);

        var vertex4 = new RectVertexData(
            new Vector2(1.0f, 1.0f),
            Vector4.Zero,
            Color.Empty,
            false,
            1f,
            0f,
            0f,
            0f,
            0f);

        return new[] { vertex1, vertex2, vertex3, vertex4 };
    }

    /// <summary>
    /// Applies the color of the given <paramref name="rect"/> shape to the rectangle
    /// data being sent to the GPU.
    /// </summary>
    /// <param name="data">The data to apply the color to.</param>
    /// <param name="rect">The rect that holds the color to apply to the data.</param>
    /// <returns>The original GPU <paramref name="data"/> with the color applied.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown if the <see cref="ColorGradient"/> of the given <paramref name="rect"/>
    ///     is an invalid value.
    /// </exception>
    private static RectGPUData ApplyColor(RectGPUData data, ShapeBatchItem rect)
    {
        switch (rect.GradientType)
        {
            case ColorGradient.None:
                return data.SetColor(rect.Color);
            case ColorGradient.Horizontal:
                data = data.SetColor(rect.GradientStart, VertexNumber.One); // BOTTOM LEFT
                data = data.SetColor(rect.GradientStart, VertexNumber.Two); // BOTTOM RIGHT
                data = data.SetColor(rect.GradientStop, VertexNumber.Three); // TOP RIGHT
                data = data.SetColor(rect.GradientStop, VertexNumber.Four); // BOTTOM RIGHT
                break;
            case ColorGradient.Vertical:
                data = data.SetColor(rect.GradientStart, VertexNumber.One); // BOTTOM LEFT
                data = data.SetColor(rect.GradientStop, VertexNumber.Two); // BOTTOM RIGHT
                data = data.SetColor(rect.GradientStart, VertexNumber.Three); // TOP RIGHT
                data = data.SetColor(rect.GradientStop, VertexNumber.Four); // BOTTOM RIGHT
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rect.GradientType), "The gradient type is invalid.");
        }

        return data;
    }

    /// <summary>
    /// Process the border thickness by checking that the value is within limits.
    /// If it is not within limits, it will force the value to be within limits.
    /// </summary>
    /// <param name="rect">The rectangle containing the border thickness to set within a limit.</param>
    /// <remarks>
    ///     This is done to prevent any undesired rendering artifacts from occuring.
    /// </remarks>
    private static ShapeBatchItem ProcessBorderThicknessLimit(ShapeBatchItem rect)
    {
        var largestValueAllowed = (rect.Width <= rect.Height ? rect.Width : rect.Height) / 2f;

        var newBorderThickness = rect.BorderThickness > largestValueAllowed
            ? largestValueAllowed
            : rect.BorderThickness;
        newBorderThickness = newBorderThickness < 1f ? 1f : newBorderThickness;

        rect = new ShapeBatchItem(
            rect.Position,
            rect.Width,
            rect.Height,
            rect.Color,
            rect.IsSolid,
            newBorderThickness,
            rect.CornerRadius,
            rect.GradientType,
            rect.GradientStart,
            rect.GradientStop);

        return rect;
    }

    /// <summary>
    /// Processes the corner radius by checking each corner radius value and making sure they
    /// are within limits.  If it is not within limits, it will force the values to be within limits.
    /// </summary>
    /// <param name="rect">The rectangle containing the radius values to process.</param>
    /// <returns>The rect with the corner radius values set within limits.</returns>
    /// <remarks>
    ///     This is done to prevent any undesired rendering artifacts from occuring.
    /// </remarks>
    private static ShapeBatchItem ProcessCornerRadiusLimits(ShapeBatchItem rect)
    {
        /*
         * Always have the smallest value between the width and height (divided by 2)
         * as the maximum limit of what any corner radius can be.
         * If the value was allowed to be larger than the smallest value between
         * the width and height, it would produce unintended rendering artifacts.
         */
        var largestValueAllowed = (rect.Width <= rect.Height ? rect.Width : rect.Height) / 2f;

        var cornerRadius = rect.CornerRadius;

        cornerRadius = cornerRadius.TopLeft > largestValueAllowed ? CornerRadius.SetTopLeft(cornerRadius, largestValueAllowed) : cornerRadius;
        cornerRadius = cornerRadius.BottomLeft > largestValueAllowed ? CornerRadius.SetBottomLeft(cornerRadius, largestValueAllowed) : cornerRadius;
        cornerRadius = cornerRadius.BottomRight > largestValueAllowed ? CornerRadius.SetBottomRight(cornerRadius, largestValueAllowed) : cornerRadius;
        cornerRadius = cornerRadius.TopRight > largestValueAllowed ? CornerRadius.SetTopRight(cornerRadius, largestValueAllowed) : cornerRadius;

        cornerRadius = cornerRadius.TopLeft < 0 ? CornerRadius.SetTopLeft(cornerRadius, 0) : cornerRadius;
        cornerRadius = cornerRadius.BottomLeft < 0 ? CornerRadius.SetBottomLeft(cornerRadius, 0) : cornerRadius;
        cornerRadius = cornerRadius.BottomRight < 0 ? CornerRadius.SetBottomRight(cornerRadius, 0) : cornerRadius;
        cornerRadius = cornerRadius.TopRight < 0 ? CornerRadius.SetTopRight(cornerRadius, 0) : cornerRadius;

        rect = new ShapeBatchItem(
            rect.Position,
            rect.Width,
            rect.Height,
            rect.Color,
            rect.IsSolid,
            rect.BorderThickness,
            cornerRadius,
            rect.GradientType,
            rect.GradientStart,
            rect.GradientStop);

        return rect;
    }
}
