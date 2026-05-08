import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { Router } from '@angular/router';
import { from } from 'rxjs';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  // Skip auth header for login/refresh endpoints
  const isAuthEndpoint = req.url.includes('/auth/login') || req.url.includes('/auth/refresh');

  const token = auth.getAccessToken();
  const authReq = (token && !isAuthEndpoint)
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authReq).pipe(
    catchError((err: HttpErrorResponse) => {
      // On 401 (expired token), try to refresh once
      if (err.status === 401 && !isAuthEndpoint) {
        return from(auth.refreshToken()).pipe(
          switchMap(refreshed => {
            if (refreshed) {
              // Retry original request with new token
              const newToken = auth.getAccessToken();
              const retryReq = req.clone({
                setHeaders: { Authorization: `Bearer ${newToken}` }
              });
              return next(retryReq);
            }
            // Refresh failed — redirect to login
            router.navigate(['/login']);
            return throwError(() => err);
          })
        );
      }

      // On 403 — redirect to monitor (access denied)
      if (err.status === 403) {
        router.navigate(['/dashboard/monitor']);
      }

      return throwError(() => err);
    })
  );
};
