#!/bin/bash
#SBATCH -p main
#SBATCH -N1
#SBATCH -c24
lscpu | grep -E 'Model name|^CPU\(s\)|Socket|Core|Thread'
./Lab3 bench 24 3
