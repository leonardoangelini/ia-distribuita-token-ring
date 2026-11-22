#!/bin/bash
# Termina tutti i nodi ATM avviati (equivale a Ctrl+C in ogni terminale).
# Funziona su macOS e Linux.
pkill -INT -f "TokenRingAtm.dll ATM[1-4] " && echo "Nodi terminati." || echo "Nessun nodo in esecuzione."
