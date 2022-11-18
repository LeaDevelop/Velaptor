﻿// <copyright file="TextureBatchingServiceTests.cs" company="KinsonDigital">
// Copyright (c) KinsonDigital. All rights reserved.
// </copyright>

// ReSharper disable UseObjectOrCollectionInitializer
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using FluentAssertions;
using Moq;
using Velaptor;
using Velaptor.Graphics;
using Velaptor.OpenGL;
using Velaptor.Reactables.Core;
using Velaptor.Reactables.ReactableData;
using Velaptor.Services;
using VelaptorTests.Helpers;
using Xunit;

namespace VelaptorTests.Services;

/// <summary>
/// Tests the <see cref="TextureBatchingService"/> class.
/// </summary>
public class TextureBatchingServiceTests
{
    private readonly Mock<IReactable<BatchSizeData>> mockBatchSizeReactable;
    private readonly Mock<IDisposable> mockUnsubscriber;
    private IReactor<BatchSizeData>? reactor;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureBatchingServiceTests"/> class.
    /// </summary>
    public TextureBatchingServiceTests()
    {
        this.mockUnsubscriber = new Mock<IDisposable>();

        this.mockBatchSizeReactable = new Mock<IReactable<BatchSizeData>>();
        this.mockBatchSizeReactable.Setup(m => m.Subscribe(It.IsAny<IReactor<BatchSizeData>>()))
            .Callback<IReactor<BatchSizeData>>(reactorObj => this.reactor = reactorObj)
            .Returns(this.mockUnsubscriber.Object);
    }

    #region Constructor Tests
    [Fact]
    public void Ctor_WithNullBatchSizeReactableParam_ThrowsException()
    {
        // Arrange & Act
        var act = () =>
        {
            _ = new TextureBatchingService(null);
        };

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("The parameter must not be null. (Parameter 'batchSizeReactable')");
    }

    [Fact]
    public void Ctor_WhenReceivingBatchSizePushNotification_CreatesBatchItemList()
    {
        // Arrange & Act
        var sut = CreateService();
        this.reactor.OnNext(new BatchSizeData(4u));

        // Assert
        sut.BatchItems.Should().HaveCount(4);
    }

    [Fact]
    public void Ctor_WhenEndNotificationsIsInvoked_UnsubscribesFromReactable()
    {
        // Arrange
        _ = CreateService();

        // Act
        this.reactor.OnCompleted();
        this.reactor.OnCompleted();

        // Assert
        this.mockUnsubscriber.Verify(m => m.Dispose());
    }
    #endregion

    #region Prop Tests
    [Fact]
    public void BatchItems_WhenSettingValue_ReturnsCorrectResult()
    {
        // Arrange
        var batchItem1 = (true, new TextureBatchItem
        {
            Angle = 1,
            Effects = RenderEffects.None,
            Size = 2,
            DestRect = new RectangleF(3, 4, 5, 6),
            SrcRect = new RectangleF(7, 8, 9, 10),
            TextureId = 11,
            TintColor = Color.FromArgb(12, 13, 14, 15),
            ViewPortSize = new SizeF(16, 17),
        });
        var batchItem2 = (true, new TextureBatchItem
        {
            Angle = 18,
            Effects = RenderEffects.FlipHorizontally,
            Size = 19,
            DestRect = new RectangleF(20, 21, 22, 23),
            SrcRect = new RectangleF(24, 25, 26, 27),
            TextureId = 28,
            TintColor = Color.FromArgb(29, 30, 31, 32),
            ViewPortSize = new SizeF(33, 34),
        });

        var batchItems = new List<(bool, TextureBatchItem)> { batchItem1, batchItem2 };
        var expected = new ReadOnlyCollection<(bool, TextureBatchItem)>(batchItems.ToReadOnlyCollection());
        var service = CreateService();

        // Act
        service.BatchItems = batchItems.ToReadOnlyCollection();
        var actual = service.BatchItems;

        // Assert
        AssertExtensions.ItemsEqual(expected.ToArray(), actual.ToArray());
        AssertExtensions.ItemsEqual(expected.ToArray(), actual.ToArray());
    }
    #endregion

    #region Method Tests
    [Fact]
    public void Add_WhenSwitchingTextures_RaisesBatchFilledEvent()
    {
        // Arrange
        var batchItem1 = default(TextureBatchItem);
        batchItem1.TextureId = 10;
        var batchItem2 = default(TextureBatchItem);
        batchItem2.TextureId = 20;

        var service = CreateService();
        this.reactor.OnNext(new BatchSizeData(100u));
        service.Add(batchItem1);

        // Act & Assert
        Assert.Raises<EventArgs>(e =>
        {
            service.ReadyForRendering += e;
        }, e =>
        {
            service.ReadyForRendering -= e;
        }, () =>
        {
            service.Add(batchItem2);
        });
    }

    [Fact]
    public void Add_WhenBatchIsFull_RaisesBatchFilledEvent()
    {
        // Arrange
        var batchItem1 = default(TextureBatchItem);
        batchItem1.TextureId = 10;
        var batchItem2 = default(TextureBatchItem);
        batchItem2.TextureId = 10;

        var service = CreateService();
        this.reactor.OnNext(new BatchSizeData(1u));
        service.Add(batchItem1);

        // Act & Assert
        Assert.Raises<EventArgs>(e =>
        {
            service.ReadyForRendering += e;
        }, e =>
        {
            service.ReadyForRendering -= e;
        }, () =>
        {
            service.Add(batchItem2);
        });
    }

    [Fact]
    public void EmptyBatch_WhenInvoked_EmptiesAllItemsReadyToRender()
    {
        // Arrange
        var batchItem1 = default(TextureBatchItem);
        batchItem1.TextureId = 10;
        var batchItem2 = default(TextureBatchItem);
        batchItem2.TextureId = 10;

        var service = CreateService();
        this.reactor.OnNext(new BatchSizeData(2u));
        service.Add(batchItem1);
        service.Add(batchItem2);

        // Act
        service.EmptyBatch();

        // Assert
        Assert.NotEqual(batchItem1, service.BatchItems[0].item);
    }

    [Fact]
    public void EmptyBatch_WithNoItemsReadyToRender_DoesNotEmptyItems()
    {
        // Arrange
        var batchItem1 = default(TextureBatchItem);
        batchItem1.TextureId = 10;
        var batchItem2 = default(TextureBatchItem);
        batchItem2.TextureId = 10;

        var service = CreateService();
        this.reactor.OnNext(new BatchSizeData(2u));
        service.BatchItems = new List<(bool, TextureBatchItem)> { (false, batchItem1), (false, batchItem2) }.ToReadOnlyCollection();

        // Act
        service.EmptyBatch();

        // Assert
        Assert.Equal(batchItem1, service.BatchItems[0].item);
        Assert.Equal(batchItem2, service.BatchItems[1].item);
    }
    #endregion

    /// <summary>
    /// Creates a new instance of <see cref="TextureBatchingService"/> for the purpose of testing.
    /// </summary>
    /// <returns>The instance to test.</returns>
    private TextureBatchingService CreateService() => new (this.mockBatchSizeReactable.Object);
}
