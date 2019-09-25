# MassPullRequest

MassPullRequest is a tool which can check out a list of github repositories and perform an action (run a bash/powershell command in each). Optionally any changes made by the script can be committed, pushed, and a a pull request submitted.

## Usage

`MassPullRequest [arguments] [options]`

## Arguments

`Command`  The command to be executed on each repostitory, executed via powershell on windows, bash elsewhere.

Options:

  `-?|-h|--help ` 
  Show help information.

  `--repo <url>`
  A set of repositories on which to execute the required action.

  `--repo-file <path>`
  A file containing a list of repository URLs on which to execute the specified action (one url per line).

  `--base-branch <branchName>`
  The branch name to be checked out before running the specified command.

  `--commit`
  Set this flag to create a commit with changes to the repository. If using this flag, a commit message must be set with --commit-message.

  `--push`
  Set this flag to push changes to the remote repository. Implies `--commit`.

  `--changes-branch <branchName>`
  The branch name on which to commit changes caused by the specified command. To be used in conjunction with `--commit` or --pull-request.

  `--commit-message <message>`
  The message to use when commiting changes. Required when using `--commit` or `--pull-request`.

  `--pull-request`
  Set this flag to create a pull request with changes to the repository. If using this flag, branch name and commit message must be set with `--changes-branch` and `--commit-message`. Implies `--push` and `--commit`.

  `--cleanup`
  Set this flag to delete the newly checked out local repo once it has been processed.