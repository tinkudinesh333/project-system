﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.IO;

using Microsoft.VisualStudio.GraphModel;
using Microsoft.VisualStudio.GraphModel.Schemas;
using Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.GraphNodes.ViewProviders;
using Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.Snapshot;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.GraphNodes.Actions
{
    /// <summary>
    ///     Base class for graph action handlers that operate on a set of input nodes,
    ///     each of which is backed by an <see cref="IDependency"/>.
    /// </summary>
    internal abstract class InputNodeGraphActionHandlerBase : GraphActionHandlerBase
    {
        protected InputNodeGraphActionHandlerBase(IAggregateDependenciesSnapshotProvider aggregateSnapshotProvider) 
            : base(aggregateSnapshotProvider)
        {
        }

        protected abstract bool CanHandle(IGraphContext graphContext);

        protected abstract void ProcessInputNode(
            IGraphContext graphContext,
            GraphNode inputGraphNode,
            IDependency dependency,
            IDependenciesSnapshot snapshot,
            IDependenciesGraphViewProvider viewProvider,
            string projectPath,
            ref bool trackChanges);

        public sealed override bool TryHandleRequest(IGraphContext graphContext)
        {
            if (!CanHandle(graphContext))
            {
                return false;
            }

            bool trackChanges = false;

            foreach (GraphNode inputGraphNode in graphContext.InputNodes)
            {
                if (graphContext.CancelToken.IsCancellationRequested)
                {
                    return trackChanges;
                }

                string projectPath = inputGraphNode.Id.GetValue(CodeGraphNodeIdName.Assembly);

                if (string.IsNullOrEmpty(projectPath))
                {
                    continue;
                }

                IDependency dependency = FindDependency(inputGraphNode, out IDependenciesSnapshot snapshot);

                if (dependency == null || snapshot == null)
                {
                    continue;
                }

                IDependenciesGraphViewProvider viewProvider = FindViewProvider(dependency);

                if (viewProvider == null)
                {
                    continue;
                }

                using (var scope = new GraphTransactionScope())
                {
                    ProcessInputNode(graphContext, inputGraphNode, dependency, snapshot, viewProvider, projectPath, ref trackChanges);

                    scope.Complete();
                }
            }

            return trackChanges;
        }

        private IDependency FindDependency(GraphNode inputGraphNode, out IDependenciesSnapshot snapshot)
        {
            string projectPath = inputGraphNode.Id.GetValue(CodeGraphNodeIdName.Assembly);

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                snapshot = null;
                return null;
            }

            string projectFolder = Path.GetDirectoryName(projectPath);

            if (projectFolder == null)
            {
                snapshot = null;
                return null;
            }

            string id = inputGraphNode.GetValue<string>(DependenciesGraphSchema.DependencyIdProperty);

            bool topLevel;

            if (id == null)
            {
                // this is top level node and it contains full path 
                id = inputGraphNode.Id.GetValue(CodeGraphNodeIdName.File);

                if (id == null)
                {
                    // No full path, so this must be a node generated by a different provider.
                    snapshot = null;
                    return null;
                }

                if (id.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase))
                {
                    int startIndex = projectFolder.Length;

                    // Trim backslashes (without allocating)
                    while (startIndex < id.Length && id[startIndex] == '\\')
                    {
                        startIndex++;
                    }

                    id = id.Substring(startIndex);
                }

                topLevel = true;
            }
            else
            {
                topLevel = false;
            }

            snapshot = AggregateSnapshotProvider.GetSnapshotProvider(projectPath)?.CurrentSnapshot;

            return snapshot?.FindDependency(id, topLevel);
        }
    }
}
