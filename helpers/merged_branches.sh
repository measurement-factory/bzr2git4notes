#! /bin/sh -e

# Among all given bzr branches, selects 'merged' branches.  A 'merged'
# branch is a bzr branch whose branch-specific revisions were merged
# into another branch(branches). During import, since other branches
# have all these revisions, the result git repository can miss these
# 'merged' branches, depending on the branches import order.

if [ -z $1 ]
then
   echo usage: $0 root_dir_name
   exit 1
fi

# a root directory with all checked out bzr branches
root_dir=$1

# temporary files
all_ids=./all_ids.out
last_ids=./last_ids.out
tmp_log=./log.out
tmp_result=./tmp_result.out

# result 'merged' branches list
branches=./branches.out

if [ -e $all_ids ]
then
    rm $all_ids
fi

if [ -e $last_ids ]
then
    rm $last_ids
fi

if [ -e $tmp_result ]
then
    rm $tmp_result
fi

for d in `ls $root_dir`
do
    dir=${root_dir}/${d}
    if [ -d $dir ]
    then
        echo Processing $dir ...
        bzr log -n0 --show-ids $dir > $tmp_log
        egrep "^( *revision-id: | *branch nick: )" $tmp_log | paste --delimiters=' ' - - | awk -v dir="$d" '{ print dir, $5, $2 }'  >> $all_ids
        egrep "^( *revision-id: | *branch nick: )" $tmp_log | paste --delimiters=' ' - - | head -1 | awk -v dir="$d" '$0~dir{ print dir, $2 }'  >> $last_ids
    fi
done

while read line
do
    nick=`echo $line | cut -d ' ' -f1`
    found=`grep "$line" $all_ids | grep -v "^$nick"`
    num=0

    if [ "$found" ]
    then
        num=`echo "$found" | wc -l`
    fi

    if [ $num -gt 0 ]
    then
        echo "$found" >> $tmp_result
    fi
done < $last_ids

cut -d ' ' -f2 $tmp_result | uniq > $branches

