/* eslint-disable @typescript-eslint/no-explicit-any */
import type { FC } from 'react'
import React, { useState } from 'react'
import { useMutation } from 'react-apollo'
import { FormattedMessage } from 'react-intl'
import { Alert, Button, Card, Divider } from 'vtex.styleguide'

import M_CLEAR_SHEET from '../mutations/ClearSheet.gql'

const ClearSheetButton: FC = () => {
  const [showAlert, setShowAlert] = useState(true)
  const [clearSheet, { loading: sheetClearing, data: sheetCleared }] =
    useMutation(M_CLEAR_SHEET)

  const displayAlert = !sheetClearing && sheetCleared && showAlert

  return (
    <div>
      {displayAlert && (
        <div className="mb2">
          <Alert
            type="success"
            onClose={() => setShowAlert(false)}
            autoClose={6000}
          >
            <FormattedMessage id="admin/sheets-catalog-import.sheet-clear.success" />
          </Alert>
        </div>
      )}
      <Card>
        <div className="flex">
          <div className="w-70">
            <p>
              <FormattedMessage id="admin/sheets-catalog-import.sheet-clear.description" />
            </p>
          </div>
          <div
            style={{ flexGrow: 1 }}
            className="flex items-stretch w-20 justify-center"
          >
            <Divider orientation="vertical" />
          </div>
          <div className="w-30 items-center flex">
            <Button
              variation="secondary"
              collapseLeft
              block
              isLoading={sheetClearing}
              onClick={() => {
                clearSheet()
                setShowAlert(true)
              }}
            >
              <FormattedMessage id="admin/sheets-catalog-import.sheet-clear.button" />
            </Button>
          </div>
        </div>
      </Card>
      <br />
    </div>
  )
}

export default ClearSheetButton
