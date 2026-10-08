using System;
using System.IO;

namespace FtpSync
{
	/// <summary>Imports a FileZilla site list: XML (sitemanager.xml, File - Export) or CSV, chosen by content/extension.</summary>
	public static class FileZillaImporter
	{
		public static NppFtpImporter.Result Import(string file)
		{
			if (string.Equals(Path.GetExtension(file), ".csv", StringComparison.OrdinalIgnoreCase)) return FileZillaCsvImporter.Import(file);
			return FileZillaXmlImporter.Import(file);
		}
	}
}
