#! /bin/sh -e

# Verifies that repository tags point to identical bzr and git sources.
# Exits with a non-zero code if at least one tag fails verification.
# Note that bzr tags are branch-based while git tags are global.

# a requred path to a checked out bzr branch:
bzr_root="$1"
shift

# a requred path to a git repository
git_root="$1"
shift

# tag names to check (optional)
tags=$@

if test -z "$tags"
then
    tags=`mktemp`
    cd $bzr_root
    tags=`bzr tags | fgrep -v '?' | sed 's/ .*//'`
    cd - > /dev/null
    echo "Automatically computed tags:"
    echo $tags
fi

result=0

for tag in $tags
do
    echo "Tag: $tag"

    cd $git_root
    git checkout --quiet tags/${tag}
    cd - > /dev/null

    cd $bzr_root
    bzr update --quiet -r tag:${tag}
    cd - > /dev/null

    if ! diff -ur -x '.git' -x '.bzr' $bzr_root $git_root
    then
        echo "ERROR: bzr and git tag ${tag} differ!"
        result=1
    fi
done

exit $result
