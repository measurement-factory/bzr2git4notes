#! /bin/sh -e

# Verifies that repository branches contain identical bzr and git sources.
# Exits with a non-zero code if at least one branch fails verification.

# Expects (bzr branch path, git branch "path") pairs on stdin. For example:
# bzr/v5 git/master

# All bzr paths must point to a checked out branch.

compare_revs=`dirname $0`/compare_revs.sh

result=0

while read bzr_path git_path
do
    echo "Comparing $bzr_path $git_path"

    # git repository directory
    export git_root=`dirname $git_path`
    # git branch name
    export git_branch=`basename $git_path`

    cd $bzr_path
    bzr log --line |
        awk -F: '{ gsub(/^ +/, "", $1); print $1 }' |
        sort > /tmp/bzr.log
    cd - > /dev/null

    cd $git_root
    git log $git_branch |
        egrep '^(commit | *Bzr-Reference:)' |
        paste --delimiters=' ' - - |
        sed -r "s/commit *|Bzr-Reference: *//g;s/  *$git_branch r/ $git_branch /" |
        sort --key 3,3 > /tmp/git.log
    cd - > /dev/null

    # SELECT bzr.revno, git.hash WHERE bzr.revno == git.revno ORDER BY bzr.revno
    # The sort order minimizes each step changes to increase checkout speed.
    join -1 1 -2 3 -o 1.1,2.1 /tmp/bzr.log /tmp/git.log |
        sort --version-sort --key 1,1 > /tmp/bzrgit.log
    if ! $compare_revs $bzr_path $git_root < /tmp/bzrgit.log
    then
        result=1
    fi
done

exit $result
