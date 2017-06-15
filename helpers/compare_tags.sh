#! /bin/sh

# Compares bzr against git Squid source trees for each of the given TAG.
# List of tags to check is provided via tags_file.

tags_file=/tmp/Squid/tags_v4.txt
# a path a specific bzr branch: 
bzr_root=/tmp/Squid/bzr/v4
# a path to the converted git repository
git_root=/tmp/Squid/git

for i in `cat $tags_file`
do
  cd $git_root
  git checkout tags/${i}
  cd $bzr_root
  bzr update -r tag:${i}
  diff -r -x '.git' -x '.bzr' $bzr_root $git_root
  echo $? $i >> ${tags_file}.log
done
