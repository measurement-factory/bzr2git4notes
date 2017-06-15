bzr2git4notes is a Bazaar to Git converter that uses git notes to preserve bzr metadata that cannot be stored in git commits.

To compile and run it under mono on Linux:

    $ mcs bzr2git4notes.cs
    $ mono bzr2git4notes.exe

The tool expects data stream produced by `bzr fast-export` command on its stdin and sends converted data with generated git notes to its stdout. For example:

    $ bzr_repo=/bzr/repos/Squid
    $ git_repo=/git/repos/squid
    $ export GIT_DIR=$git_repo/.git
    $ bzr fast-export --no-plain $bzr_repo/trunk |
      mono bzr2git4notes.exe |
      git fast-import

A typical project repository has several partially overlapping branches. Converting the entire repository requires keeping export/import state between individual bzr branch conversions. That state consists of standard "marks" files and bzr2git4notes internal data. The `bzr2git.sh` script hides these details from the user:

    $ bzr_repo=/bzr/repos/Squid
    $ git_repo=/git/repos/squid

    # The first step: import bzr v5.0 branch into git master branch.
    $ ./bzr2git.sh $bzr_repo/5 $git_repo/master

    # Further steps: import a few more version branches...
    $ ./bzr2git.sh $bzr_repo/4 $git_repo/v4
    $ ./bzr2git.sh $bzr_repo/3.5 $git_repo/v3.5
