﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;
using MoonSharp.Interpreter.Grammar;

namespace MoonSharp.Interpreter.Tree.Expressions
{
	class FunctionDefinitionExpression : Expression, IClosureBuilder
	{
		SymbolRef[] m_ParamNames = null;
		Statement m_Statement;
		RuntimeScopeFrame m_StackFrame;
		List<SymbolRef> m_Closure = new List<SymbolRef>();
		bool m_HasVarArgs = false;
		Instruction m_ClosureInstruction = null;

		public object UpvalueCreationTag { get; set; }

		public FunctionDefinitionExpression(LuaParser.AnonfunctiondefContext context, ScriptLoadingContext lcontext, bool pushSelfParam = false)
			: this(context.funcbody(), lcontext, pushSelfParam)
		{
		}

		public SymbolRef CreateUpvalue(BuildTimeScope scope, SymbolRef symbol)
		{
			for (int i = 0; i < m_Closure.Count; i++)
			{
				if (m_Closure[i].i_Name == symbol.i_Name)
				{
					return SymbolRef.Upvalue(symbol.i_Name, i);
				}
			}

			m_Closure.Add(symbol);

			if (m_ClosureInstruction != null)
			{
				m_ClosureInstruction.SymbolList = m_Closure.ToArray();
			}

			return SymbolRef.Upvalue(symbol.i_Name, m_Closure.Count - 1);
		}

		public FunctionDefinitionExpression(LuaParser.FuncbodyContext context, ScriptLoadingContext lcontext, bool pushSelfParam = false)
			: base(context, lcontext)
		{
			var parlist = context.parlist();
			List<string> paramnames = new List<string>();

			if (pushSelfParam)
			{
				paramnames.Add("self");
			}


			if (parlist != null)
			{
				var namelist = parlist.namelist();

				if (namelist != null)
				{
					paramnames.AddRange(namelist.NAME()
						.Select(t => t.GetText()));
				}
			}

			m_HasVarArgs = (parlist != null && parlist.vararg() != null);

			if (m_HasVarArgs)
				paramnames.Add("...");

			lcontext.Scope.PushFunction(this);

			m_ParamNames = DefineArguments(paramnames, lcontext);

			m_Statement = NodeFactory.CreateStatement(context.block(), lcontext);

			m_StackFrame = lcontext.Scope.PopFunction();

		}

		private SymbolRef[] DefineArguments(List<string> paramnames, ScriptLoadingContext lcontext)
		{
			SymbolRef[] ret = new SymbolRef[paramnames.Count];

			for (int i = 0; i < paramnames.Count; i++)
				ret[i] = lcontext.Scope.DefineLocal(paramnames[i]);

			return ret;
		}

		public int CompileBody(ByteCode bc, string friendlyName)
		{
			Instruction I = bc.Emit_Jump(OpCode.Jump, -1);

			bc.Emit_BeginFn(m_StackFrame, friendlyName ?? "<anonymous>");

			int entryPoint = bc.GetJumpPointForLastInstruction();

			if (m_ParamNames.Length > 0)
				bc.Emit_Args(m_ParamNames);

			m_Statement.Compile(bc);

			bc.Emit_Ret(0);

			I.NumVal = bc.GetJumpPointForNextInstruction();

			return entryPoint;
		}

		public int Compile(ByteCode bc, Action afterDecl, string friendlyName)
		{
			SymbolRef[] symbs = m_Closure
				//.Select((s, idx) => s.CloneLocalAndSetFrame(m_ClosureFrames[idx]))
				.ToArray();

			m_ClosureInstruction = bc.Emit_Closure(symbs, bc.GetJumpPointForNextInstruction() + 3);
			afterDecl();
			return CompileBody(bc, friendlyName);
		}


		public override void Compile(ByteCode bc)
		{
			Compile(bc, () => bc.Emit_Nop(null), null);
		}

	}
}
