using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiamiGraphics.Core.Parser
{

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public enum ActionType { Import, Replace, Delete }

	public class PatchAction
	{
		public ActionType Type { get; set; }
		public string TargetPath { get; set; }
		public string SourcePath { get; set; }
		public long Size { get; set; }
		public string Sha256 { get; set; }
		public bool IsWholeReplaceNestedRpf { get; set; }
	}

	public class DiffManifest
	{
		public string ReduxName { get; set; }
		public DateTime ParsedAt { get; set; }
		public long TotalPatchSize { get; set; }
		public List<PatchAction> Actions { get; set; } = new List<PatchAction>();
	}

	public class ContentXmlInfo
	{
		public List<string> AllFilesToEnable { get; set; } = new List<string>();
		public List<string> CustomRpfs { get; set; } = new List<string>();
		public List<string> DefaultRpfs { get; set; } = new List<string>();

		public string ParseError { get; set; }
	}

	public class ResolvedComponentMap
	{
		public Dictionary<string, ComponentInfo> Components { get; set; } = new Dictionary<string, ComponentInfo>();
	}

	public class ComponentInfo
	{
		public bool IsFound { get; set; }
		public string SourceRpf { get; set; }
		public List<string> InternalPaths { get; set; } = new List<string>();
		public List<string> Flags { get; set; } = new List<string>();
	}
}
