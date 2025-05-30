﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.ComponentModel;
using MS.Internal;
using System.Windows.Media.Animation;
using System.Windows.Markup;

namespace System.Windows.Media
{
    #region ImageSource

    /// <summary>
    /// Interface for Bitmap Sources, included decoders and effects
    /// </summary>
    [TypeConverter(typeof(System.Windows.Media.ImageSourceConverter))]
    [ValueSerializer(typeof(ImageSourceValueSerializer))]
    [Localizability(LocalizationCategory.None, Readability = Readability.Unreadable)]
    public abstract partial class ImageSource : Animatable
    {
        #region Constructor

        /// <summary>
        /// Don't allow 3rd party extensibility.
        /// </summary>
        internal ImageSource()
        {
        }


        #endregion Constructor

        /// <summary>
        /// Get the width of the image in measure units (96ths of an inch).
        /// </summary>
        public abstract double Width
        {
            get;
        }

        /// <summary>
        /// Get the height of the image in measure units (96ths of an inch).
        /// </summary>
        public abstract double Height
        {
            get;
        }

        /// <summary>
        /// Get the metadata associated with this image source
        /// </summary>
        public abstract ImageMetadata Metadata
        {
            get;
        }

        /// <summary>
        /// Get the Size associated with this image source
        /// </summary>
        internal virtual Size Size
        {
            get
            {
                return new Size(Width, Height);            
            }
        }

        #region ToInstanceDescriptor
        /// <summary>
        /// Can serialze "this" to a string
        /// </summary>
        internal virtual bool CanSerializeToString()
        {
            return false;
        }

        #endregion     

        /// <summary>
        /// Converts pixels to DIPs in a way consistent with MIL. Protected here is okay
        /// because ImageSource isn't extensible by 3rd parties.
        /// </summary>
        protected static double PixelsToDIPs(double dpi, int pixels)
        {
            // Obtain the natural size in MIL Device Independant Pixels (DIPs, or 1/96") of the bitmap.
            // This is: (Bitmap Pixels) / (Bitmap DotsPerInch) * (DIPs per inch)

            float dpif = (float)dpi;

            // To be consistent with BitmapBrush
            //
            // Floating-point precision is used to maintain consistent
            // logic with BitmapBrush DPI scaling, which is implemented in
            // unmanaged code using single-precision math.  Any changes to
            // this logic must also be updated in the UCE BitmapBrush
            // resource to maintain this consistency.

            if (dpif < 0.0F || FloatUtil.IsCloseToDivideByZero(96.0F, dpif))
            {
                return pixels;
            }

            return (double)(pixels * (96.0F / dpif));
        }
    }

    #endregion // ImageSource
}

