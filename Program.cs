// <copyright file="Program.cs" company="Cognisant Research">
// Copyright (c) Cognisant Research. All rights reserved.
// </copyright>

namespace MassPullRequest
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using LibGit2Sharp;
    using Microsoft.Extensions.CommandLineUtils;
    using Octokit;
    using Branch = LibGit2Sharp.Branch;
    using Repository = LibGit2Sharp.Repository;
    using Signature = LibGit2Sharp.Signature;

    public static class Program
    {
        public static void Main(string[] args)
        {
            var app = new CommandLineApplication();
            app.Name = "MassPullRequest";
            app.Description = "Clones then executes an action accross a number of GitHub repositories, optionally committing and creatign a PR with any changes";

            app.HelpOption("-?|-h|--help");

            var command = app.Argument("Command", "The command to be executed on each repostitory, executed via powershell on windows, bash elsewhere.", false);

            var repos = app.Option(
                "--repo <url>",
                "A set of repositories on which to execute the required action.",
                CommandOptionType.MultipleValue);

            var reposFile = app.Option(
                "--repo-file <path>",
                "A file containing a list of repository URLs on which to execute the specified action (one url per line).",
                CommandOptionType.SingleValue);

            var baseBranch = app.Option(
                "--base-branch <branchName>",
                "The branch name to be checked out before running the specified command.",
                CommandOptionType.SingleValue);

            var doCommit = app.Option(
                "--commit",
                "Set this flag to create a commit with changes to the repository. If using this flag, a commit message must be set with --commit-message.",
                CommandOptionType.NoValue);

            var doPush = app.Option(
                "--push",
                "Set this flag to push changes to the remote repository.",
                CommandOptionType.NoValue);

            var changesBranch = app.Option(
                "--changes-branch <branchName>",
                "The branch name on which to commit changes caused by the specified command. To be used in conjunction with --commit or --pull-request.",
                CommandOptionType.SingleValue);

            var message = app.Option(
                "--commit-message <message>",
                "The message to use when commiting changes. Required when using --commit or --pull-request.",
                CommandOptionType.SingleValue);

            var doPullRequest = app.Option(
                "--pull-request",
                "Set this flag to create a pull request with changes to the repository. If using this flag, branch name and commit message must be set with --changes-branch and --commit-message. Implies --commit and --push.",
                CommandOptionType.NoValue);

            var doCleanup = app.Option(
                "--cleanup",
                "Set this flag to delete the newly checked out local repo once it has been processed.",
                CommandOptionType.NoValue);

            app.OnExecute(() => Run(
                app,
                repos.Values,
                reposFile.Value(),
                doCommit.HasValue() || doPush.HasValue() || doPullRequest.HasValue(), // PR and push imply commit
                baseBranch.Value(),
                changesBranch.Value(),
                message.Value(),
                doPush.HasValue() || doPullRequest.HasValue(), // PR implies push
                doPullRequest.HasValue(),
                doCleanup.HasValue(),
                command.Value));

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

        private static int Run(CommandLineApplication app, List<string> repos, string reposFile, bool doCommit, string baseBranchName, string changesBranchName, string commitMessage, bool push, bool createPr, bool cleanup, string commandToRun)
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

            if (doCommit && string.IsNullOrWhiteSpace(commitMessage))
            {
                Console.Error.WriteLine("Error: Specify a commit message with --commit-message when using --commit or --pull-request");
                app.ShowHelp();
                return 1;
            }

            if (createPr && string.IsNullOrWhiteSpace(changesBranchName))
            {
                Console.Error.WriteLine("Error: Specify a branch to create with --changes-branch when using --pull-request");
                app.ShowHelp();
                return 1;
            }

            var credentials = GetCredentials();

            foreach (var repoUrl in urls)
            {
                RepositoryProcessor processor = null;
                try
                {
                    processor = new RepositoryProcessor(repoUrl, credentials);

                    if (!string.IsNullOrWhiteSpace(baseBranchName))
                    {
                        processor.CheckoutBaseBranch(baseBranchName);
                    }

                    if (!string.IsNullOrWhiteSpace(changesBranchName))
                    {
                        processor.CreateChangesBranch(changesBranchName);
                    }

                    processor.RunCommand(commandToRun);

                    if (!doCommit || !processor.HasChanges)
                    {
                        continue;
                    }

                    processor.Commit(commitMessage);

                    if (push)
                    {
                        processor.Push();
                    }

                    if (createPr)
                    {
                        processor.CreatePullRequest(commitMessage);
                    }
                }
                finally
                {
                    // Wether success or failure, clean up the cloned repository
                    if (processor != null)
                    {
                        processor.Cleanup();
                    }
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

        private static string GetHiddenConsoleInput()
        {
            StringBuilder input = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);
                }
                else if (key.Key != ConsoleKey.Backspace)
                {
                    input.Append(key.KeyChar);
                }
            }

            return input.ToString();
        }

        private static bool TryLoadUrls(List<string> repos, string reposFile, out IReadOnlyList<Uri> urls)
        {
            urls = new List<Uri>();
            var repoList = new List<string>();

            repoList.AddRange(repos);

            if (!string.IsNullOrWhiteSpace(reposFile))
            {
                if (File.Exists(reposFile))
                {
                    repoList.AddRange(File.ReadAllLines(reposFile).Where(l => !string.IsNullOrWhiteSpace(l)));
                }
                else
                {
                    Console.Error.WriteLine($"Error: Repo file not found at {reposFile}.");
                    return false;
                }
            }

            var invalidUris = repoList.Where(x => !Uri.TryCreate(x, UriKind.Absolute, out _)).ToList();

            if (invalidUris.Any())
            {
                foreach (var uri in invalidUris)
                {
                    Console.Error.WriteLine($"Error: Invalid URI {uri}");
                }

                return false;
            }

            urls = repoList.Select(x => new Uri(x)).ToList();
            return urls.Any();
        }
    }
}
