using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.CommandLineUtils;

namespace MassPullRequest
{
    class Program
    {
        private static int Run(CommandLineApplication app, CommandOption repos, CommandOption reposFile) {
                if(!TryLoadUrls(repos, reposFile, out var urls)) {
                    return 1;
                }

                foreach(var url in urls) {
                    Console.WriteLine(url.ToString());
                }
                return 0;
        }

        private static bool TryLoadUrls(CommandOption repos, CommandOption reposFile, out IReadOnlyList<Uri> urls) {
                urls = new List<Uri>();
                var repoList = new List<string>();

                repoList.AddRange(repos.Values);
                
                if(reposFile.HasValue()) {
                    var filePath = reposFile.Value();
                    if(File.Exists(filePath)) {
                        repoList.AddRange(File.ReadAllLines(filePath).Where(l => !string.IsNullOrWhiteSpace(l)));
                    } else {
                        Console.WriteLine($"ERROR: Repo file not found at {filePath}.");
                        return false;
                    }
                }

                var invalidUris = repoList.Where(x => !Uri.TryCreate(x, UriKind.Absolute, out _));

                if(invalidUris.Any()) {
                    foreach(var uri in invalidUris) {
                        Console.WriteLine($"ERROR: Invalid URI {uri}");
                    }
                    return false;
                }

                urls = repoList.Select(x => new Uri(x)).ToList();
                return true;
        }

        static void Main(string[] args)
        {
            var app = new CommandLineApplication();
            app.Name = "MassPullRequest";
            app.Description = "Clones then executes an action accross a number of GitHub repositories, optionally committing and creatign a PR with any changes";

            app.HelpOption("-?|-h|--help");

            var repos = app.Option("-r|--repo <url>", 
                    "A set of repositories on which to execute the required action.",
                    CommandOptionType.MultipleValue);

            var reposFile = app.Option("-rf|--repo-file|--repos-file <path>", "A file containing a list of repository URLs on which to execute the specified action (one url per line).", CommandOptionType.SingleValue);

            app.OnExecute(() => Run(app, repos, reposFile));

            try
            {
                app.Execute(args);
            }
            catch (CommandParsingException ex)
            {
                // You'll always want to catch this exception, otherwise it will generate a messy and confusing error for the end user.
                // the message will usually be something like:
                // "Unrecognized command or argument '<invalid-command>'"
                Console.WriteLine(ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to execute application: {0}", ex.Message);
            }
        }
    }
}
