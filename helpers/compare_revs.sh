#! /bin/sh -e

# Verifies that repository commits point to identical bzr and git sources.
# Exits with a non-zero code if at least one commit fails verification.
# Note that bzr revisions are branch-based while git hashes are global.

# Expects (bzr revision number, git hash) pairs on stdin. For example:
# 14000 d2167ca720605f7b857c8ff75f1c5ecfe8c9823e

# a requred path to a checked out bzr branch:
bzr_root="$1"

# a requred path to a git repository
git_root="$2"

result=0

while read revno hash
do
    cd $git_root
    git checkout --quiet --detach $hash
    cd - > /dev/null

    cd $bzr_root
    bzr update --quiet -r${revno}
    cd - > /dev/null

    if ! diff -ur -x '.git' -x '.bzr' $bzr_root $git_root
    then
        echo "ERROR: bzr r${revno} differs from git $hash"
        result=1
    fi
done

exit $result
