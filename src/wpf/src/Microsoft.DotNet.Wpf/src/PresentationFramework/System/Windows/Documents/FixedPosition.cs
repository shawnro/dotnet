// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

//
// Description:
//      FixedPosition represents a hit-testable position in a fixed document's tree.
//

namespace System.Windows.Documents
{
    //=====================================================================
    /// <summary>
    ///      FixedPosition represents a hit-testable position in a fixed document's tree.
    /// </summary>
    internal struct FixedPosition
    {
        //--------------------------------------------------------------------
        //
        // Connstructors
        //
        //---------------------------------------------------------------------

        #region Constructors
        internal FixedPosition(FixedNode fixedNode, int offset)
        {
            _fixedNode  = fixedNode;
            _offset     = offset;
        }
        #endregion Constructors

        //--------------------------------------------------------------------
        //
        // Public Methods
        //
        //---------------------------------------------------------------------

#if DEBUG
        /// <summary>
        /// Create a string representation of this object
        /// </summary>
        /// <returns>string - A string representation of this object</returns>
        public override string ToString()
        {
            return string.Create(CultureInfo.InvariantCulture, $"FN[{_fixedNode}]-Offset[{_offset}]");
        }
#endif


        //--------------------------------------------------------------------
        //
        // Public Properties
        //
        //---------------------------------------------------------------------

        //--------------------------------------------------------------------
        //
        // Public Events
        //
        //---------------------------------------------------------------------

        //--------------------------------------------------------------------
        //
        // Internal Methods
        //
        //---------------------------------------------------------------------


        //--------------------------------------------------------------------
        //
        // Internal Properties
        //
        //---------------------------------------------------------------------

        #region Internal Properties
        //
        internal int Page
        {
            get
            {
                return _fixedNode.Page;
            }
        }

        //
        internal FixedNode Node
        {
            get
            {
                return _fixedNode;
            }
        }

        internal int Offset
        {
            get
            {
                return _offset;
            }
        }
        #endregion Internal Properties

        //--------------------------------------------------------------------
        //
        // Private Methods
        //
        //---------------------------------------------------------------------

        #region Private Properties
        #endregion Private Properties

        //--------------------------------------------------------------------
        //
        // Private Fields
        //
        //---------------------------------------------------------------------
        #region Private Fields
        private readonly FixedNode _fixedNode;
        private readonly int       _offset;      // offset into the fixed node
        #endregion Private Fields
    }
}
