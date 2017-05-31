bzr2git4notes is a Bazaar to Git converter that uses git notes to preserve bzr metadata
that cannot be stored in git commits.

To compile and run it under mono on Linux:

    $ mcs bzr2git4notes.cs
    $ mono bzr2git4notes.exe

The tool expects data stream produced by 'bzr fast-export' command on its stdin
and sends converted data with generated git notes to its stdout. For example:

    $ trunk_path=/path/to/bzr/trunk
    $ bzr fast-export --no-plain --import-marks=./marks.bzr $trunk_path |
      mono bzr2git4notes.exe | GIT_DIR=project/.git git fast-import --import-marks=./marks.git

A helper bzr2git.sh script simplifies converting several branches into a single git repository.
Before running, the script should be adjusted by specifying paths to repositories and
converted branch names.