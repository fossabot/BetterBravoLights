﻿using System;
using System.Collections.Generic;

namespace BravoLights.Ast
{
    /// <summary>
    /// A node which represents a constant value.
    /// </summary>
    abstract class ConstantNode<T>: IAstNode
    {
        protected ConstantNode(T value)
        {
            Value = value;
        }

        public readonly T Value;

        public string ErrorText { get { return null; } }

        public IEnumerable<IVariable> Variables
        {
            get { return new IVariable[0]; }
        }

        public event EventHandler<ValueChangedEventArgs> ValueChanged
        {
            add { value(this, new ValueChangedEventArgs { NewValue = Value }); }
            remove { }
        }
    }
}