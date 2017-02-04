﻿// ******************************************************************
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THE CODE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
// THE CODE OR THE USE OR OTHER DEALINGS IN THE CODE.
// ******************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media.Animation;

namespace Microsoft.Toolkit.Uwp.UI.Controls
{
    public class Position
    {
        public Position(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; set; }
        public int Column { get; set; }
    }

    /// <summary>
    /// The AdaptiveGridView control allows to present information within a Grid View perfectly adjusting the
    /// total display available space. It reacts to changes in the layout as well as the content so it can adapt
    /// to different form factors automatically.
    /// </summary>
    /// <remarks>
    /// The number and the width of items are calculated based on the
    /// screen resolution in order to fully leverage the available screen space. The property ItemsHeight define
    /// the items fixed height and the property DesiredWidth sets the minimum width for the elements to add a
    /// new column.</remarks>
    public partial class AdaptiveGridView : GridView
    {
        private bool _isLoaded;

        private bool _isAnimated;
        private readonly Dictionary<FrameworkElement, Position> _visibleItemContainers = new Dictionary<FrameworkElement, Position>();
        private readonly Vector3KeyFrameAnimation _scaleAnimation;
        private readonly ScalarKeyFrameAnimation _opacityAnimation;

        // Can be made as dependency properties
        private const int InitialDelay = 400;
        private const int AnimationDuration = 360;
        private const int AnimationDelay = 100;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdaptiveGridView"/> class.
        /// </summary>
        public AdaptiveGridView()
        {
            IsTabStop = false;
            SizeChanged += OnSizeChanged;
            ItemClick += OnItemClick;
            if (Items != null) Items.VectorChanged += ItemsOnVectorChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            // Define ItemContainerStyle in code rather than using the DefaultStyle
            // to avoid having to define the entire style of a GridView. This can still
            // be set by the enduser to values of their chosing
            var style = new Style(typeof(GridViewItem));
            style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            ItemContainerStyle = style;

            // Set up opacity and scale animations.
            var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            _scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
            _scaleAnimation.Duration = TimeSpan.FromMilliseconds(AnimationDuration);
            _scaleAnimation.InsertKeyFrame(0, new Vector3(0.8f, 0.8f, 0));
            _scaleAnimation.InsertKeyFrame(1, Vector3.One, EaseOutCubic(compositor));
            _opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
            _opacityAnimation.Duration = TimeSpan.FromMilliseconds(AnimationDuration);
            _opacityAnimation.InsertKeyFrame(1, 1.0f, EaseOutCubic(compositor));

            // Remove the default entrance transition if existed.
            RegisterPropertyChangedCallback(ItemContainerTransitionsProperty, (s, e) =>
            {
                var entranceThemeTransition = ItemContainerTransitions.OfType<EntranceThemeTransition>().SingleOrDefault();
                if (entranceThemeTransition != null)
                {
                    ItemContainerTransitions.Remove(entranceThemeTransition);
                }
            });
        }

        /// <summary>
        /// Prepares the specified element to display the specified item.
        /// </summary>
        /// <param name="obj">The element that's used to display the specified item.</param>
        /// <param name="item">The item to display.</param>
        protected override async void PrepareContainerForItemOverride(DependencyObject obj, object item)
        {
            base.PrepareContainerForItemOverride(obj, item);
            var element = obj as FrameworkElement;
            if (element == null) return;

            var heightBinding = new Binding
            {
                Source = this,
                Path = new PropertyPath("ItemHeight"),
                Mode = BindingMode.TwoWay
            };

            var widthBinding = new Binding
            {
                Source = this,
                Path = new PropertyPath("ItemWidth"),
                Mode = BindingMode.TwoWay
            };

            element.SetBinding(HeightProperty, heightBinding);
            element.SetBinding(WidthProperty, widthBinding);

            var itemsWrapGrid = ItemsPanelRoot as ItemsWrapGrid;
            if (itemsWrapGrid == null) return;

            var numberOfColumns = (int)Math.Round(ActualWidth / ItemWidth);
            var index = IndexFromContainer(obj);
            var rowIndex = index % numberOfColumns;
            var colIndex = index / numberOfColumns;

            if (itemsWrapGrid.LastVisibleIndex <= 0)
            {
                ElementCompositionPreview.GetElementVisual(element).Opacity = 0;
                _visibleItemContainers.Add(element, new Position(rowIndex, colIndex));

                // When all items are visible, run the staggered animation in the end.
                if (!_isAnimated && index == Items?.Count - 1)
                {
                    await RunStaggeredAnimationOnItemContainersAsync(numberOfColumns);
                }
            }
            else
            {
                if (_isAnimated) return;
                _isAnimated = true;

                // Only run the staggered animation once when all visible items are rendered.
                await RunStaggeredAnimationOnItemContainersAsync(numberOfColumns);
            }
        }

        private async Task RunStaggeredAnimationOnItemContainersAsync(int numberOfColumns)
        {
            await Task.Delay(InitialDelay);

            var numberOfRows = Math.Ceiling(ActualHeight / ItemHeight);
            var last = (numberOfRows - 1) + (numberOfColumns - 1);

            for (var i = 0; i <= last; i++)
            {
                var sum = i;
                var containers = _visibleItemContainers.Where(c => c.Value.Row + c.Value.Column == sum).Select(k => k.Key);

                foreach (var container in containers)
                {
                    var containerVisual = ElementCompositionPreview.GetElementVisual(container);
                    _scaleAnimation.DelayTime = _opacityAnimation.DelayTime = TimeSpan.FromMilliseconds(AnimationDelay * i);
                    containerVisual.CenterPoint = new Vector3((float)ItemWidth, (float)ItemHeight, 0) * 0.5f;
                    containerVisual.StartAnimation(nameof(Visual.Scale), _scaleAnimation);
                    containerVisual.StartAnimation(nameof(Visual.Opacity), _opacityAnimation);
                }
            }
        }

        private static CubicBezierEasingFunction EaseOutCubic(Compositor compositor) =>
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.215f, 0.61f), new Vector2(0.355f, 1.0f));

        /// <summary>
        /// Calculates the width of the grid items.
        /// </summary>
        /// <param name="containerWidth">The width of the container control.</param>
        /// <returns>The calculated item width.</returns>
        protected virtual double CalculateItemWidth(double containerWidth)
        {
            var desiredWidth = double.IsNaN(DesiredWidth) ? containerWidth : DesiredWidth;

            var columns = CalculateColumns(containerWidth, desiredWidth);

            // If there's less items than there's columns, reduce the column count (if requested);
            if (Items != null && Items.Count > 0 && Items.Count < columns && StretchContentForSingleRow)
            {
                columns = Items.Count;
            }

            return (containerWidth / columns) - 5;
        }

        /// <summary>
        /// Invoked whenever application code or internal processes (such as a rebuilding layout pass) call
        /// ApplyTemplate. In simplest terms, this means the method is called just before a UI element displays
        /// in your app. Override this method to influence the default post-template logic of a class.
        /// </summary>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            OnOneRowModeEnabledChanged(this, OneRowModeEnabled);
        }

        private void ItemsOnVectorChanged(IObservableVector<object> sender, IVectorChangedEventArgs @event)
        {
            if (!double.IsNaN(ActualWidth))
            {
                // If the item count changes, check if more or less columns needs to be rendered,
                // in case we were having fewer items than columns.
                RecalculateLayout(ActualWidth);
            }
        }

        private void OnItemClick(object sender, ItemClickEventArgs e)
        {
            var cmd = ItemClickCommand;
            if (cmd != null)
            {
                if (cmd.CanExecute(e.ClickedItem))
                {
                    cmd.Execute(e.ClickedItem);
                }
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // If the width of the internal list view changes, check if more or less columns needs to be rendered.
            if (!e.PreviousSize.Width.Equals(e.NewSize.Width))
            {
                RecalculateLayout(e.NewSize.Width);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            DetermineOneRowMode();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void DetermineOneRowMode()
        {
            if (_isLoaded)
            {
                var itemsWrapGridPanel = ItemsPanelRoot as ItemsWrapGrid;

                if (OneRowModeEnabled)
                {
                    var b = new Binding
                    {
                        Source = this,
                        Path = new PropertyPath("ItemHeight")
                    };

                    if (itemsWrapGridPanel != null)
                    {
                        itemsWrapGridPanel.Orientation = Orientation.Vertical;
                    }

                    SetBinding(MaxHeightProperty, b);

                    ScrollViewer.SetVerticalScrollMode(this, ScrollMode.Disabled);
                    ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
                    ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Visible);
                    ScrollViewer.SetHorizontalScrollMode(this, ScrollMode.Enabled);
                }
                else
                {
                    this.ClearValue(MaxHeightProperty);
                    if (itemsWrapGridPanel != null)
                    {
                        itemsWrapGridPanel.Orientation = Orientation.Horizontal;
                    }

                    ScrollViewer.SetVerticalScrollMode(this, ScrollMode.Enabled);
                    ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Visible);
                    ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
                    ScrollViewer.SetHorizontalScrollMode(this, ScrollMode.Disabled);
                }
            }
        }

        private void RecalculateLayout(double containerWidth)
        {
            if (containerWidth > 0)
            {
                var newWidth = CalculateItemWidth(containerWidth);

                if (double.IsNaN(ItemWidth) || Math.Abs(newWidth - ItemWidth) > 1)
                {
                    ItemWidth = newWidth;
                }
            }
        }
    }
}