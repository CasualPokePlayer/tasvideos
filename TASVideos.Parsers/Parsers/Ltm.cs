using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SharpCompress.Readers;
using TASVideos.MovieParsers.Result;

namespace TASVideos.MovieParsers.Parsers
{
	[FileExtension("ltm")]
	internal class Ltm : ParserBase, IParser
	{
		public override string FileExtension => "ltm";

		private void DumpToConsole(TextReader r)
		{
			while (r.ReadLine() is string s)
			{
				Debug.WriteLine(s);
			}
		}

		public IParseResult Parse(Stream file)
		{
			var result = new ParseResult
			{
				Region = RegionType.Ntsc,
				FileExtension = FileExtension
			};

			using (var reader = ReaderFactory.Open(file))
			{
				while (reader.MoveToNextEntry())
				{
					if (reader.Entry.IsDirectory)
					{
						continue;
					}

					using (var entry = reader.OpenEntryStream())
					using (var textReader = new StreamReader(entry))
					{
						switch (reader.Entry.Key)
						{
							case "config.ini":
								while (textReader.ReadLine() is string s)
								{
									if (s.StartsWith("frame_count"))
									{
										result.Frames = ParseIntFromConfig(s);
									}
								}
								break;
							case "inputs":
								// also a text file, input roll stuff
								Debug.WriteLine("##INPUTS##:");
								DumpToConsole(textReader);
								break;
						}

						entry.SkipEntry(); // seems to be required if the stream was not fully consumed
					}
				}
			}

			return result;
		}

		private int ParseIntFromConfig(string str)
		{
			if (string.IsNullOrWhiteSpace(str))
			{
				return 0;
			}

			var split = str.Split(new []{ "="}, StringSplitOptions.RemoveEmptyEntries);

			if (split.Length > 1)
			{
				var intStr = split.Skip(1).First();
				var result = int.TryParse(intStr, out int val);
				if (result)
				{
					return val;
				}
			}

			return 0;
		}
	}
}