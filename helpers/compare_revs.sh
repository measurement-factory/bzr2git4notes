#! /bin/sh -e

# Verifies that repository commits point to identical bzr and git sources.
# Exits with a non-zero code if at least one commit fails verification.
# Note that bzr revisions are branch-based while git hashes are global.

# Expects (git hash, bzr revision) pairs on stdin. For example:
# d2167ca720605f7b857c8ff75f1c5ecfe8c9823e r14000

# a requred path to a checked out bzr branch:
bzr_root="$1"

# a requred path to a git repository
git_root="$2"

result=0

while read sha rev
do
    cd $git_root
    git checkout --quiet --detach $sha
    cd - > /dev/null

    cd $bzr_root
    bzr update --quiet -${rev}
    cd - > /dev/null

    if ! diff -ur -x '.git' -x '.bzr' $bzr_root $git_root
    then
        echo "ERROR: git $sha differs from bzr ${rev}!"
        result=1
    fi
done

exit $result
