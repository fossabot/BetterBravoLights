﻿
using System;
using System.Collections.Generic;

namespace BravoLights.Common.Ast
{
    abstract class BinaryExpression<TOutput> : IAstNode
    {
        internal readonly IAstNode Lhs;
        internal readonly IAstNode Rhs;

        private object lastLhsValue;
        private object lastRhsValue;
        private object lastReportedValue;

        protected BinaryExpression(IAstNode lhs, IAstNode rhs)
        {
            Lhs = lhs;
            Rhs = rhs;
        }

        public string ErrorText => null;

        public IEnumerable<IVariable> Variables
        {
            get
            {
                foreach (var variable in Lhs.Variables) {
                    yield return variable;
                }
                foreach (var variable in Rhs.Variables)
                {
                    yield return variable;
                }
            }
        }

        protected abstract string OperatorText { get; }
        protected abstract object ComputeValue(object lhsValue, object rhsValue);
       
        private void HandleLhsValueChanged(object sender, ValueChangedEventArgs e)
        {
            lastLhsValue = e.NewValue;

            Recompute();
        }

        private void HandleRhsValueChanged(object sender, ValueChangedEventArgs e)
        {
            lastRhsValue = e.NewValue;

            Recompute();
        }

        private void Recompute()
        {
            if (lastLhsValue == null || lastRhsValue == null)
            {
                return;
            }

            object newValue = ComputeValue(lastLhsValue, lastRhsValue);

            if (lastReportedValue == null || !lastReportedValue.Equals(newValue)) // N.B. We must unbox before doing the comparison otherwise we'll be comparing boxed pointers
            {
                lastReportedValue = newValue;

                listeners?.Invoke(this, new ValueChangedEventArgs { NewValue = newValue });
            }
        }

        private EventHandler<ValueChangedEventArgs> listeners;

        public event EventHandler<ValueChangedEventArgs> ValueChanged
        {
            add
            {
                var subscribe = listeners == null;
                // It's important that we add the listener before subscribing to children
                // because the subscription may fire immediately
                listeners += value;
                if (subscribe)
                {
                    Lhs.ValueChanged += HandleLhsValueChanged;
                    Rhs.ValueChanged += HandleRhsValueChanged;
                }
                if (lastReportedValue != null)
                {
                    // New subscriber and we already have a valid computed value. Ship it.
                    value(this, new ValueChangedEventArgs { NewValue = lastReportedValue });
                }
            }
            remove
            {
                listeners -= value;
                if (listeners == null)
                {
                    Lhs.ValueChanged -= HandleLhsValueChanged;
                    Rhs.ValueChanged -= HandleRhsValueChanged;
                }
            }
        }

        public override string ToString()
        {
            return $"({Lhs} {OperatorText} {Rhs})";
        }
    }
}
