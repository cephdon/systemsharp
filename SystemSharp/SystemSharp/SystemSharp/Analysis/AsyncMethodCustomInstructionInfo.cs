﻿/**
 * Copyright 2013 Christian Köllner
 * 
 * This file is part of System#.
 *
 * System# is free software: you can redistribute it and/or modify it under 
 * the terms of the GNU Lesser General Public License (LGPL) as published 
 * by the Free Software Foundation, either version 3 of the License, or (at 
 * your option) any later version.
 *
 * System# is distributed in the hope that it will be useful, but WITHOUT ANY
 * WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS 
 * FOR A PARTICULAR PURPOSE. See the GNU General Public License for more 
 * details.
 *
 * You should have received a copy of the GNU General Public License along 
 * with System#. If not, see http://www.gnu.org/licenses/lgpl.html.
 * */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using SystemSharp.Components;

namespace SystemSharp.Analysis
{
    /// <summary>
    /// This class modifies the behavior of ILInstruction info in the context of calling asynchronous methods:
    /// It detects the code pattern "call get_IsCompleted; brtrue ...". In those cases, brtrue is not classified as a conditional
    /// branch but as either no branch or unconditional branch, depending on whether get_IsCompleted is assumed to return always true
    /// or always false.
    /// </summary>
    class AsyncMethodCustomInstructionInfo: ILInstructionInfo
    {
        /// <summary>
        /// Assumption on IsCompleted property
        /// </summary>
        public enum EAssumption
        {
            /// <summary>
            /// IsCompleted is assumed to return always true.
            /// </summary>
            AlwaysCompleted,

            /// <summary>
            /// IsCompleted is assumed to return always false.
            /// </summary>
            NeverCompleted
        }

        private MethodCode _cfg;
        private EAssumption _assumption;
        private Dictionary<int, int> _branchOverrides;

        /// <summary>
        /// Constructs a new instance based on a control-flow graph and an assumption on IsCompleted properties.
        /// </summary>
        /// <param name="cfg">a control-flow graph</param>
        /// <param name="assumption">whether the IsCompleted property is assumed to return always true or always false</param>
        public AsyncMethodCustomInstructionInfo(MethodCode cfg, EAssumption assumption):
            base(cfg.Method)
        {
            _cfg = cfg;
            _assumption = assumption;
            _branchOverrides = new Dictionary<int, int>();
            MarkIsCompletedBranches();
        }

        private void MarkIsCompletedBranches()
        {
            foreach (var ili in _cfg.Instructions)
            {
                if (ili.Code.Value == OpCodes.Call.Value ||
                    ili.Code.Value == OpCodes.Callvirt.Value)
                {
                    var mb = (MethodBase)ili.Operand;
                    if (mb.HasCustomOrInjectedAttribute<RewriteIsCompleted>())
                    {
                        var brtrue = _cfg.Instructions[ili.Index + 1];
                        if (brtrue.Code.Value != OpCodes.Brtrue.Value &&
                            brtrue.Code.Value != OpCodes.Brtrue_S.Value)
                        {
                            throw new NotSupportedException("Unexpected get_IsCompleted / branch pattern!");
                        }
                        switch (_assumption)
                        {
                            case EAssumption.AlwaysCompleted:
                                _branchOverrides[brtrue.Index] = _cfg.InstructionInfo[(int)brtrue.Operand].Index;
                                break;

                            case EAssumption.NeverCompleted:
                                _branchOverrides[brtrue.Index] = brtrue.Index + 1;
                                break;

                            default:
                                throw new NotImplementedException();
                        }
                    }
                }
            }
        }

        public override EBranchBehavior IsBranch(SDILReader.ILInstruction i, out IEnumerable<int> targets)
        {
            if (_branchOverrides.ContainsKey(i.Index))
            {
                targets = Enumerable.Repeat(_branchOverrides[i.Index], 1);
                return _assumption == EAssumption.AlwaysCompleted ? EBranchBehavior.UBranch : EBranchBehavior.NoBranch;
            }
            else
            {
                return base.IsBranch(i, out targets);
            }
        }
    }
}
