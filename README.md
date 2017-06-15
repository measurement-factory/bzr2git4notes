bzr2git4notes is a Bazaar to Git converter that uses git notes to preserve bzr
metadata that cannot be stored in git commits.

To compile and run it under mono on Linux:

    $ mcs bzr2git4notes.cs
    $ mono bzr2git4notes.exe

The tool expects data stream produced by 'bzr fast-export' command on its stdin
and sends converted data with generated git notes to its stdout. For example:

    $ trunk_path=/path/to/bzr/trunk
    $ bzr fast-export --no-plain  $trunk_path | mono bzr2git4notes.exe |
      GIT_DIR=project/.git git fast-import

Converting several unmerged bzr branches with common history is slightly more
complicated because each iteration produces a context used in the next
iteration. This context includes 'marks' files and bzr2git4notes internal data.
To simplify process, there is a helper bzr2git.sh script, hiding these details
from the user. Before running, the script should be adjusted by specifying paths
to bzr and git repositories. The first run should import git 'master' from bzr
'trunk'. After that, all other branches are imported.
For example:

# first step: import v5.0 into 'master'
bzr2git.sh --bzr-branch 5.0 --git-branch master

# second step: import v4.0 and v3.5 branches
bzr2git.sh --bzr-branch 4.0 --git-branch 4.0
bzr2git.sh --bzr-branch 3.5 --git-branch 3.5


