// <copyright file="RepositoryProcessor.cs" company="Cognisant Research">
// Copyright (c) Cognisant Research. All rights reserved.
// </copyright>

namespace MassPullRequest
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using LibGit2Sharp;
    using Octokit;
    using Branch = LibGit2Sharp.Branch;
    using Repository = LibGit2Sharp.Repository;
    using Signature = LibGit2Sharp.Signature;

    public class RepositoryProcessor
    {
        private readonly Uri _repositoryUrl;
        private readonly string _clonePath;
        private readonly UsernamePasswordCredentials _credentials;
        private Repository _repository;
        private Branch _baseBranch;
        private Branch _changesBranch;

        public RepositoryProcessor(Uri repositoryUrl, UsernamePasswordCredentials credentials)
        {
            _repositoryUrl = repositoryUrl;
            _credentials = credentials;

            Console.WriteLine($"Cloning {_repositoryUrl}");

            _clonePath = Path.Combine(".", _repositoryUrl.ToString().Split("/").Last(x => !string.IsNullOrWhiteSpace(x)));
            var repoPath = Repository.Clone(
                _repositoryUrl.ToString(),
                _clonePath,
                new CloneOptions { CredentialsProvider = (x, y, z) => _credentials });

            _repository = new Repository(repoPath);
            _baseBranch = _repository.Branches[_repository.Head.FriendlyName];
        }

        public bool HasChanges =>
            _repository == null
                ? false
                : _repository.RetrieveStatus(new LibGit2Sharp.StatusOptions()).Any();

        public void CheckoutBaseBranch(string branchName)
        {
            var localBranch = _repository.Branches[branchName];

            if (localBranch == null)
            {
                // Probably a remote branch
                Branch trackedBranch = _repository.Branches[$"origin/{branchName}"];

                if (trackedBranch == null)
                {
                    throw new Exception($"Branch {branchName} not found locally or on origin.");
                }

                localBranch = _repository.CreateBranch(branchName, trackedBranch.Tip);
                _repository.Branches.Update(localBranch, b => b.UpstreamBranch = $"refs/heads/{branchName}");
            }

            Commands.Checkout(_repository, _repository.Branches[branchName]);
            _baseBranch = localBranch;
            _changesBranch = localBranch;
        }

        public void CreateChangesBranch(string changesBranch)
        {
            // need to make sure that the changes branch name is unique both locally and remote
            // if branch name is taken we'll try branchname-2, branchname-3 etc
            var branchName = changesBranch;
            Branch localBranch;
            Branch trackedBranch;

            var i = 0;
            do
            {
                i++;
                branchName = $"{changesBranch}{(i == 1 ? string.Empty : "-" + i)}";
                localBranch = _repository.Branches[branchName];
                trackedBranch = _repository.Branches[$"origin/{branchName}"];
            }
            while (localBranch != null || trackedBranch != null);

            if (i != 1)
            {
                Console.WriteLine($"Branch name {changesBranch} was in use, checked out {changesBranch}-{i} instead.");
            }

            localBranch = _repository.CreateBranch(branchName);
            _repository.Branches.Update(localBranch, b => b.UpstreamBranch = $"refs/heads/{branchName}");

            Commands.Checkout(_repository, _repository.Branches[branchName]);
            _changesBranch = localBranch;
        }

        public void RunCommand(string command)
        {
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
                    WorkingDirectory = Path.Combine(".", _clonePath),
                },
            };

            process.OutputDataReceived += (sender, data) => Console.WriteLine(data.Data);
            process.ErrorDataReceived += (sender, data) => Console.WriteLine(data.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }

        public void Commit(string commitMessage)
        {
            var numberOfChanges = _repository.RetrieveStatus(new LibGit2Sharp.StatusOptions()).Count();
            Console.WriteLine($"Committing {numberOfChanges} changed files.");

            Commands.Stage(_repository, "*"); // equivalent of "git add ."

            // We need to specify the author for the commit, grab it from config
            Configuration config = _repository.Config;
            Signature author = config.BuildSignature(DateTimeOffset.Now);
            _repository.Commit(commitMessage, author, author);
        }

        public void Push()
        {
            Console.WriteLine("Pushing changes.");

            _repository.Branches.Update(_changesBranch, b =>
            {
                b.UpstreamBranch = _changesBranch.CanonicalName;
                b.Remote = _repository.Network.Remotes["origin"].Name;
            });

            PushOptions options = new PushOptions() { CredentialsProvider = (x, y, z) => _credentials };

            _repository.Network.Push(_changesBranch, options);
        }

        public void CreatePullRequest(string pullRequestTitle)
        {
            // we will need to use octokit for this
            Console.WriteLine($"Sending pull request for {_changesBranch.FriendlyName} -> {_baseBranch.FriendlyName}");

            var githubCredentials = new Octokit.Credentials(_credentials.Username, _credentials.Password);

            var client = new GitHubClient(new ProductHeaderValue("MassPullRequest")) { Credentials = githubCredentials };

            // octokit can only get a repo by owner + name, so we need to chop up the url
            var urlParts = _repositoryUrl.AbsolutePath.Split("/").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            var repoName = urlParts.Last();
            var owner = urlParts[urlParts.Count - 2];

            var pr = new NewPullRequest(pullRequestTitle, _changesBranch.Reference.CanonicalName, _baseBranch.Reference.CanonicalName);

            client.PullRequest.Create(owner, repoName, pr).Wait();
        }

        public void Cleanup()
        {
            if (!string.IsNullOrWhiteSpace(_clonePath))
            {
                new DirectoryInfo(_clonePath).Delete(true);
            }
        }
    }
}
