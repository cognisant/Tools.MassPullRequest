using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.CommandLineUtils;

namespace MassPullRequest
{
    class Program
    {
        private static int Run(CommandLineApplication app, CommandOption repos, CommandOption reposFile, bool doCommit, CommandOption changesBranch, CommandOption commitMessage, bool createPr) {
            if(!TryLoadUrls(repos, reposFile, out var urls)) {
                return 1;
            } else if (!urls.Any()) {
                Console.WriteLine("No repository urls provided.");
                app.ShowHelp();
                return 1;
            }

            foreach(var url in urls) {
                Console.WriteLine(url.ToString());
            }

            if(doCommit && !commitMessage.HasValue()) {
                Console.WriteLine("Specify a commit message with --commit-message when using --commit or --pull-request");
                app.ShowHelp();
                return 1;
            }

            if(createPr && !changesBranch.HasValue()) {
                Console.WriteLine("Specify a branch to create with --changes-branch when using --pull-request");
                app.ShowHelp();
                return 1;
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
            return urls.Any();
        }

        static void Main(string[] args)
        {
            var app = new CommandLineApplication();
            app.Name = "MassPullRequest";
            app.Description = "Clones then executes an action accross a number of GitHub repositories, optionally committing and creatign a PR with any changes";

            app.HelpOption("-?|-h|--help");

            app.Argument("Command", "The command to be executed on each repostitory", false);

            var repos = app.Option("--repo <url>", 
                    "A set of repositories on which to execute the required action.",
                    CommandOptionType.MultipleValue);

            var reposFile = app.Option("--repo-file <path>", "A file containing a list of repository URLs on which to execute the specified action (one url per line).", CommandOptionType.SingleValue);

            var doCommit = app.Option("--commit", "Set this flag to create a commit with changes to the repository. If using this flag, a commit message must be set with -m.", CommandOptionType.NoValue);
            var createPullRequest = app.Option("--pull-request","Set this flag to create a pull request with changes to the repository. If using this flag, branch name and commit message must be set with -b and -m. Implies -c.", CommandOptionType.NoValue);

            var baseBranch = app.Option("--base-branch <branchName>", "The branch name to be checked out before running the specified command", CommandOptionType.SingleValue);
            var changesBranch = app.Option("--changes-branch <branchName>", "The branch name on which to commit changes caused by the specified command. To be used in conjunction with --commit or --pull-request", CommandOptionType.SingleValue);
            var message = app.Option("--commit-message <message>","The message to use when commiting changes. Required when using --commit or --pull-request", CommandOptionType.SingleValue);

            app.OnExecute(() => Run(app, repos, reposFile, doCommit.HasValue() || createPullRequest.HasValue(), changesBranch, message, createPullRequest.HasValue()));

            try
            {
                app.Execute(args);
            }
            catch (CommandParsingException ex)
            {
                Console.WriteLine(ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to execute application: {0}", ex.Message);
            }
        }
    }
}
