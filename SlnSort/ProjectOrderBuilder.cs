// Copyright (c) Carl Reinke
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SlnSort
{
    internal sealed class ProjectOrderBuilder
    {
        private const string _folderProjectTypeGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

        private readonly Dictionary<string, (string TypeGuid, string Name)> _projectInfos = new Dictionary<string, (string, string)>();

        private readonly Dictionary<string, string> _projectParents = new Dictionary<string, string>();

        public void SetProjectInfo(string projectGuid, string projectTypeGuid, string projectName)
        {
            _projectInfos[projectGuid] = (projectTypeGuid, projectName);
        }

        public void SetProjectParent(string projectGuid, string parentProjectGuid)
        {
            _projectParents[projectGuid] = parentProjectGuid;
        }

        public IComparer<string> Build()
        {
            var projects = new List<ProjectEntry>();

            foreach (var entry in _projectInfos)
            {
                var nestingPath = new List<NestingPathSegment>();

                string? key = entry.Key;
                var info = entry.Value;
                for (; ; )
                {
                    nestingPath.Add(new NestingPathSegment(info.TypeGuid == _folderProjectTypeGuid, info.Name));

                    if (!_projectParents.TryGetValue(key, out key) ||
                        !_projectInfos.TryGetValue(key, out info))
                    {
                        break;
                    }
                }

                nestingPath.Reverse();

                projects.Add(new ProjectEntry(entry.Key, nestingPath));
            }

            projects.Sort((x, y) =>
            {
                int minCount = Math.Min(x.NestingPath.Count, y.NestingPath.Count);
                for (int i = 0; i < minCount; ++i)
                {
                    var xSegment = x.NestingPath[i];
                    var ySegment = y.NestingPath[i];

                    int result = xSegment.IsFolder.CompareTo(ySegment.IsFolder);
                    if (result != 0)
                        return -result;

                    result = string.CompareOrdinal(xSegment.Name, ySegment.Name);
                    if (result != 0)
                        return result;
                }

                return x.NestingPath.Count.CompareTo(y.NestingPath.Count);
            });

            var result = new Dictionary<string, int>();
            for (int i = 0; i < projects.Count; ++i)
                result.Add(projects[i].Key, i);
            return new Comparer(result);
        }

        [DebuggerDisplay("{Key} = {NestingPathDebuggerDisplay}")]
        private readonly struct ProjectEntry
        {
            public readonly string Key;

            public readonly List<NestingPathSegment> NestingPath;

            public ProjectEntry(string key, List<NestingPathSegment> nestingPath)
            {
                Key = key;
                NestingPath = nestingPath;
            }

            internal string NestingPathDebuggerDisplay
            {
                get
                {
                    var builder = new StringBuilder();
                    foreach (var segment in NestingPath)
                    {
                        _ = builder.Append(segment.Name);
                        if (segment.IsFolder)
                            _ = builder.Append('/');
                    }
                    return builder.ToString();
                }
            }
        }

        private readonly struct NestingPathSegment
        {
            public readonly bool IsFolder;

            public readonly string Name;

            public NestingPathSegment(bool isFolder, string name)
            {
                IsFolder = isFolder;
                Name = name;
            }
        }

        private sealed class Comparer : IComparer<string>
        {
            private readonly Dictionary<string, int> _order;

            public Comparer(Dictionary<string, int> order)
            {
                _order = order;
            }

            public int Compare(string? x, string? y)
            {
                Debug.Assert(x != null);
                Debug.Assert(y != null);

                bool xFound = _order.TryGetValue(x, out int xOrder);
                bool yFound = _order.TryGetValue(y, out int yOrder);

                return xFound != yFound
                    ? -xFound.CompareTo(yFound)
                    : xOrder.CompareTo(yOrder);
            }
        }
    }
}
