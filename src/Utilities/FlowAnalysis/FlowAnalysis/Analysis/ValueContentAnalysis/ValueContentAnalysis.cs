﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using Analyzer.Utilities;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.PointsToAnalysis;

namespace Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.ValueContentAnalysis
{
    using ValueContentAnalysisResult = DataFlowAnalysisResult<ValueContentBlockAnalysisResult, ValueContentAbstractValue>;
    using CopyAnalysisResult = DataFlowAnalysisResult<CopyAnalysis.CopyBlockAnalysisResult, CopyAnalysis.CopyAbstractValue>;

    /// <summary>
    /// Dataflow analysis to track value content of <see cref="AnalysisEntity"/>/<see cref="IOperation"/>.
    /// </summary>
    public partial class ValueContentAnalysis : ForwardDataFlowAnalysis<ValueContentAnalysisData, ValueContentAnalysisContext, ValueContentAnalysisResult, ValueContentBlockAnalysisResult, ValueContentAbstractValue>
    {
        private ValueContentAnalysis(ValueContentAnalysisDomain analysisDomain, ValueContentDataFlowOperationVisitor operationVisitor)
            : base(analysisDomain, operationVisitor)
        {
        }

        public static ValueContentAnalysisResult? TryGetOrComputeResult(
            ControlFlowGraph cfg,
            ISymbol owningSymbol,
            WellKnownTypeProvider wellKnownTypeProvider,
            AnalyzerOptions analyzerOptions,
            DiagnosticDescriptor rule,
            PointsToAnalysisKind defaultPointsToAnalysisKind,
            CancellationToken cancellationToken,
            InterproceduralAnalysisKind interproceduralAnalysisKind = InterproceduralAnalysisKind.None,
            bool pessimisticAnalysis = true)
        {
            return TryGetOrComputeResult(cfg, owningSymbol, wellKnownTypeProvider, analyzerOptions, rule,
                defaultPointsToAnalysisKind, cancellationToken, out var _, out var _, interproceduralAnalysisKind,
                pessimisticAnalysis);
        }

        public static ValueContentAnalysisResult? TryGetOrComputeResult(
            ControlFlowGraph cfg,
            ISymbol owningSymbol,
            WellKnownTypeProvider wellKnownTypeProvider,
            AnalyzerOptions analyzerOptions,
            DiagnosticDescriptor rule,
            PointsToAnalysisKind defaultPointsToAnalysisKind,
            CancellationToken cancellationToken,
            out CopyAnalysisResult? copyAnalysisResult,
            out PointsToAnalysisResult? pointsToAnalysisResult,
            InterproceduralAnalysisKind interproceduralAnalysisKind = InterproceduralAnalysisKind.None,
            bool pessimisticAnalysis = true,
            bool performCopyAnalysisIfNotUserConfigured = false,
            InterproceduralAnalysisPredicate? interproceduralAnalysisPredicate = null,
            ImmutableArray<INamedTypeSymbol> additionalSupportedValueTypes = default,
            Func<IOperation, ValueContentAbstractValue>? getValueContentValueForAdditionalSupportedValueTypeOperation = null)
        {
            Debug.Assert(!analyzerOptions.IsConfiguredToSkipAnalysis(rule, owningSymbol, wellKnownTypeProvider.Compilation, cancellationToken));

            var interproceduralAnalysisConfig = InterproceduralAnalysisConfiguration.Create(
                analyzerOptions, rule, owningSymbol, wellKnownTypeProvider.Compilation, interproceduralAnalysisKind, cancellationToken);
            return TryGetOrComputeResult(cfg, owningSymbol, analyzerOptions, wellKnownTypeProvider,
                pointsToAnalysisKind: analyzerOptions.GetPointsToAnalysisKindOption(rule, owningSymbol, wellKnownTypeProvider.Compilation, defaultPointsToAnalysisKind, cancellationToken),
                interproceduralAnalysisConfig, out copyAnalysisResult,
                out pointsToAnalysisResult, pessimisticAnalysis,
                performCopyAnalysis: analyzerOptions.GetCopyAnalysisOption(rule, owningSymbol, wellKnownTypeProvider.Compilation, defaultValue: performCopyAnalysisIfNotUserConfigured, cancellationToken),
                interproceduralAnalysisPredicate,
                additionalSupportedValueTypes,
                getValueContentValueForAdditionalSupportedValueTypeOperation);
        }

        internal static ValueContentAnalysisResult? TryGetOrComputeResult(
            ControlFlowGraph cfg,
            ISymbol owningSymbol,
            AnalyzerOptions analyzerOptions,
            WellKnownTypeProvider wellKnownTypeProvider,
            PointsToAnalysisKind pointsToAnalysisKind,
            InterproceduralAnalysisConfiguration interproceduralAnalysisConfig,
            out CopyAnalysisResult? copyAnalysisResult,
            out PointsToAnalysisResult? pointsToAnalysisResult,
            bool pessimisticAnalysis = true,
            bool performCopyAnalysis = false,
            InterproceduralAnalysisPredicate? interproceduralAnalysisPredicate = null,
            ImmutableArray<INamedTypeSymbol> additionalSupportedValueTypes = default,
            Func<IOperation, ValueContentAbstractValue>? getValueContentValueForAdditionalSupportedValueTypeOperation = null)
        {
            copyAnalysisResult = null;
            pointsToAnalysisResult = pointsToAnalysisKind != PointsToAnalysisKind.None ?
                PointsToAnalysis.PointsToAnalysis.TryGetOrComputeResult(cfg, owningSymbol, analyzerOptions, wellKnownTypeProvider, pointsToAnalysisKind, out copyAnalysisResult,
                    interproceduralAnalysisConfig, interproceduralAnalysisPredicate, pessimisticAnalysis, performCopyAnalysis) :
                null;

            if (cfg == null)
            {
                Debug.Fail("Expected non-null CFG");
                return null;
            }

            var analysisContext = ValueContentAnalysisContext.Create(
                ValueContentAbstractValueDomain.Default, wellKnownTypeProvider, cfg, owningSymbol, analyzerOptions,
                interproceduralAnalysisConfig, pessimisticAnalysis, copyAnalysisResult,
                pointsToAnalysisResult, TryGetOrComputeResultForAnalysisContext,
                additionalSupportedValueTypes, getValueContentValueForAdditionalSupportedValueTypeOperation,
                interproceduralAnalysisPredicate);
            return TryGetOrComputeResultForAnalysisContext(analysisContext);
        }

        private static ValueContentAnalysisResult? TryGetOrComputeResultForAnalysisContext(ValueContentAnalysisContext analysisContext)
        {
            var analysisDomain = new ValueContentAnalysisDomain(analysisContext.PointsToAnalysisResult);
            var operationVisitor = new ValueContentDataFlowOperationVisitor(analysisDomain, analysisContext);
            var nullAnalysis = new ValueContentAnalysis(analysisDomain, operationVisitor);
            return nullAnalysis.TryGetOrComputeResultCore(analysisContext, cacheResult: true);
        }

        protected override ValueContentAnalysisResult ToResult(ValueContentAnalysisContext analysisContext, ValueContentAnalysisResult dataFlowAnalysisResult)
            => dataFlowAnalysisResult;

        protected override ValueContentBlockAnalysisResult ToBlockResult(BasicBlock basicBlock, ValueContentAnalysisData blockAnalysisData)
            => new ValueContentBlockAnalysisResult(basicBlock, blockAnalysisData);
    }
}
