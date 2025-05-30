// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Windows.Input.StylusWisp;
using System.Windows.Input.StylusPointer;

namespace System.Windows.Input.StylusPlugIns
{
    /// <summary>
    /// Collection of StylusPlugIn objects
    /// </summary>
    /// <remarks>
    /// The collection order is based on the order that StylusPlugIn objects are
    /// added to the collection via the IList interfaces. The order of the StylusPlugIn
    /// objects in the collection is modifiable.
    /// Some of the methods are designed to be called from both the App thread and the Pen thread,
    /// but some of them are supposed to be called from one thread only. Please look at the 
    /// comments of each method for such an information.
    /// </remarks>
    internal abstract class StylusPlugInCollectionBase
    {
        #region Static Factory Methods

        internal static StylusPlugInCollectionBase Create(StylusPlugInCollection wrapper)
        {
            StylusPlugInCollectionBase instance;

            if (StylusLogic.IsPointerStackEnabled)
            {
                instance = new PointerStylusPlugInCollection();
            }
            else
            {
                instance = new WispStylusPlugInCollection();
            }

            instance.Wrapper = wrapper;

            return instance;
        }

        #endregion

        #region Properties

        internal StylusPlugInCollection Wrapper { get; private set; }

        internal abstract bool IsActiveForInput { get; }

        internal abstract object SyncRoot { get; }

        #endregion

        #region Functions

        internal abstract void UpdateState(UIElement element);

        internal abstract void Unhook();

        #endregion
    }
}
