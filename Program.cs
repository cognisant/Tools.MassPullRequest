using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LibGit2Sharp;
using Microsoft.Extensions.CommandLineUtils;

namespace MassPullRequest
{
    class Program
    {
        private static int Run(
            CommandLineApplication app, CommandOption repos, CommandOption reposFile, bool doCommit, CommandOption baseBranchName,
            CommandOption changesBranchName, CommandOption commitMessage, bool createPr, string commandToRun)
        {
            if (!TryLoadUrls(repos, reposFile, out var urls))
            {
                return 1;
            }
            else if (!urls.Any())
            {
                Console.Error.WriteLine("Error: No repository urls provided.");
                app.ShowHelp();
                return 1;
            }

            if (doCommit && !commitMessage.HasValue())
            {
                Console.Error.WriteLine("Error: Specify a commit message with --commit-message when using --commit or --pull-request");
                app.ShowHelp();
                return 1;
            }

            if (createPr && !changesBranchName.HasValue())
            {
                Console.Error.WriteLine("Error: Specify a branch to create with --changes-branch when using --pull-request");
                app.ShowHelp();
                return 1;
            }

            var credentials = GetCredentials();

            foreach (var repoUrl in urls)
            {
                var repo = CloneRepository(repoUrl, credentials);

                if (baseBranchName.HasValue())
                {
                    CheckoutBaseBranch(repo, baseBranchName.Value());
                }

                RunCommand(repoUrl, repo, commandToRun);

                var changes = repo.RetrieveStatus(new LibGit2Sharp.StatusOptions());
                if (!doCommit || !changes.Any())
                {
                    continue;
                }

                Console.WriteLine($"Committing {changes.Count()} changed files.");
                var changesBranch = CheckoutChangesBranch(repo, changesBranchName.Value());
                CreateCommit(repo, commitMessage.Value(), credentials);
                PushChanges(repo, changesBranch, credentials);

                if (createPr)
                {
                    CreatePullRequest(repoUrl, repo, changesBranchName.Value(), credentials, baseBranchName.Value());
                }
            }

            return 0;
        }

        private static UsernamePasswordCredentials GetCredentials()
        {
            Console.Write("Enter git username: ");
            var userName = Console.ReadLine();
            Console.Write("Enter git password: ");
            var password = GetHiddenConsoleInput();
            Console.WriteLine();

            var credentials = new UsernamePasswordCredentials() { Username = userName, Password = password };
            return credentials;
        }

        private static Repository CloneRepository(Uri url, UsernamePasswordCredentials credentials) {
            Console.WriteLine($"Cloning {url}");
            var co = new CloneOptions();
            
            co.CredentialsProvider = (_, __, ___) => credentials;
            var cloneDirectory = url.ToString().Split("/").Last(x => !string.IsNullOrWhiteSpace(x));
            var repoPath = Repository.Clone(url.ToString(), cloneDirectory, co);
            return new Repository(repoPath);
        }

        private static void CheckoutBaseBranch(Repository repo, string branchName) {
            var localBranch = repo.Branches[branchName];

            if(localBranch == null) {
                // Probably a remote branch
                Branch trackedBranch = repo.Branches[$"origin/{branchName}"];

                if(trackedBranch == null) {
                    throw new Exception($"Branch {branchName} not found locally or on origin.");
                }

                localBranch = repo.CreateBranch(branchName, trackedBranch.Tip);
                repo.Branches.Update(localBranch, b => b.UpstreamBranch = $"refs/heads/{branchName}");
            }

            Commands.Checkout(repo, repo.Branches[branchName]);
        }

        private static void RunCommand(Uri url, Repository repo, string command) {
            var cloneDirectory = url.ToString().Split("/").Last(x => !string.IsNullOrWhiteSpace(x));
            var runner = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell" : "/bin/bash";

            var escapedArgs = command.Replace("\"", "\\\"");

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = runner,
                    Arguments = $"{(runner == "powershell" ? "-Command" : "-c")} \"{escapedArgs}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.Combine(".", cloneDirectory),
                }
            };
            
            process.OutputDataReceived += (sender, data) => Console.WriteLine(data.Data);
            process.ErrorDataReceived += (sender, data) => Console.WriteLine(data.Data);
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }

        private static Branch CheckoutChangesBranch(Repository repo, string changesBranch) {
            //need to make sure that the changes branch name is unique both locally and remote
            var branchName = changesBranch;
            Branch localBranch;
            Branch trackedBranch;

            var i = 0;
            do {
                i++;
                // if branch name is taken we'll try branchname-2, branchname-3 etc
                branchName = $"{changesBranch}{(i==1 ? "" : "-" + i)}";
                localBranch = repo.Branches[branchName];
                trackedBranch = repo.Branches[$"origin/{branchName}"];
            } while (localBranch != null || trackedBranch != null);

            if(i != 1) {
                Console.WriteLine($"Branch name {changesBranch} was in use, checked out {changesBranch}-{i} instead.");
            }

            localBranch = repo.CreateBranch(branchName);
            repo.Branches.Update(localBranch, b => b.UpstreamBranch = $"refs/heads/{branchName}");

            Commands.Checkout(repo, repo.Branches[branchName]);

            return localBranch;
        }

        private static void CreateCommit(Repository repo, string commitMessage, UsernamePasswordCredentials credentials) 
        {
            Commands.Stage(repo, "*"); //equivalent of "git add ."

            // Get author from config
            Configuration config = repo.Config;
            Signature author = config.BuildSignature(DateTimeOffset.Now);
            repo.Commit(commitMessage, author, author);
        }

        private static void PushChanges(Repository repo, Branch branchToPush, UsernamePasswordCredentials credentials) 
        {   
            Console.WriteLine("Pushing changes.");
            repo.Branches.Update(branchToPush,
                 b => {
                     b.UpstreamBranch = branchToPush.CanonicalName;
                     b.Remote = repo.Network.Remotes["origin"].Name;
                });

            LibGit2Sharp.PushOptions options = new LibGit2Sharp.PushOptions() {
                CredentialsProvider = (_,__,___) => credentials,
            };
            
            repo.Network.Push(branchToPush, options);
        }

        private static void CreatePullRequest(Uri url, Repository checkoutDir, string changesBranch, UsernamePasswordCredentials credentials, string baseBranch = null) {
            // we will need to use octokit for this
            Console.WriteLine($"Sending pull request for {url} {changesBranch} -> {baseBranch ?? "<default>"}");

        }

        private static string GetHiddenConsoleInput()
        {
            StringBuilder input = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace && input.Length > 0) input.Remove(input.Length - 1, 1);
                else if (key.Key != ConsoleKey.Backspace) input.Append(key.KeyChar);
            }
            return input.ToString();
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
                    Console.Error.WriteLine($"Error: Repo file not found at {filePath}.");
                    return false;
                }
            }

            var invalidUris = repoList.Where(x => !Uri.TryCreate(x, UriKind.Absolute, out _));

            if(invalidUris.Any()) {
                foreach(var uri in invalidUris) {
                    Console.Error.WriteLine($"Error: Invalid URI {uri}");
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

            var command = app.Argument("Command", "The command to be executed on each repostitory", false);

            var repos = app.Option("--repo <url>", 
                    "A set of repositories on which to execute the required action.",
                    CommandOptionType.MultipleValue);

            var reposFile = app.Option("--repo-file <path>", "A file containing a list of repository URLs on which to execute the specified action (one url per line).", CommandOptionType.SingleValue);

            var doCommit = app.Option("--commit", "Set this flag to create a commit with changes to the repository. If using this flag, a commit message must be set with --commit-message.", CommandOptionType.NoValue);
            var createPullRequest = app.Option("--pull-request","Set this flag to create a pull request with changes to the repository. If using this flag, branch name and commit message must be set with --changes-branch and --commit-message. Implies --commit and --push.", CommandOptionType.NoValue);

            var baseBranch = app.Option("--base-branch <branchName>", "The branch name to be checked out before running the specified command", CommandOptionType.SingleValue);
            var changesBranch = app.Option("--changes-branch <branchName>", "The branch name on which to commit changes caused by the specified command. To be used in conjunction with --commit or --pull-request", CommandOptionType.SingleValue);
            var message = app.Option("--commit-message <message>","The message to use when commiting changes. Required when using --commit or --pull-request", CommandOptionType.SingleValue);

            app.OnExecute(() => Run(app, repos, reposFile, doCommit.HasValue() || createPullRequest.HasValue(), baseBranch, changesBranch, message, 
                                    createPullRequest.HasValue(), command.Value));

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
                Console.Error.WriteLine("Error: {0}", ex.Message);
            }
        }
    }
}
