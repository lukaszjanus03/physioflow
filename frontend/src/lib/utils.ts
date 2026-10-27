import { clsx, type ClassValue } from 'clsx';
import { format } from 'date-fns';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function formatDate(
  date: Date | string,
  pattern: string = 'dd.MM.yyyy'
): string {
  return format(
    typeof date === 'string' ? new Date(date) : date,
    pattern
  );
}

export function formatDateTime(date: Date | string): string {
  return formatDate(date, 'dd.MM.yyyy HH:mm');
}